using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;
using SQLitePass.Core.Crypto;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;
using SQLitePass.Desktop.Services;
using SQLitePass.UI.Services;

namespace SQLitePass.Desktop;

internal sealed record EntryRow(Guid Id, long Revision, string Title, string UserName, string Url);
internal sealed record GroupChoice(PwGroup? Folder, string Name)
{
    public Guid? Id => Folder?.Id;
    public bool IsRecycleBin => Folder?.IsRecycleBin == true;
    public override string ToString() => Name;
}

public sealed partial class VaultWindow : Window
{
    private static readonly FilePickerFileType VaultFileType = new("SQLitePass") { Patterns = ["*.kpd", "*.db"] };
    private readonly AppSettings _settings;
    private readonly string _language;
    private readonly DispatcherTimer _idleTimer;
    private readonly IDesktopSession? _desktopSession;
    private VaultSession? _session;
    private CancellationTokenSource? _operation;
    private TaskCompletionSource? _operationFinished;
    private Window? _ownedDialog;
    private EntryPageQuery _query = new();
    private EntryPageCursor? _previous;
    private EntryPageCursor? _next;
    private bool _closing;
    private bool _permitClose;
    private bool _minimized;
    private bool _systemLocked;
    private bool _canCancelOperation;
    private long _lastInput = Stopwatch.GetTimestamp();

    public VaultWindow() : this(new AppSettings()) { }

    public VaultWindow(AppSettings settings, bool useNativeSession = false)
    {
        _settings = settings;
        _language = TranslationCatalog.ResolveLanguage(settings.Language, CultureInfo.CurrentUICulture);
        InitializeComponent();

        VaultPathLabel.Text = Text("LoginDatabaseFile");
        VaultPath.Text = settings.LastOpenedVaultPath;
        BrowseVault.Content = BrowseKeyFile.Content = Text("Browse");
        PasswordLabel.Text = Text("LoginMasterPassword");
        KeyFileLabel.Text = Text("LoginUseKeyFile");
        Unlock.Content = Text("LoginUnlock");
        NewVault.Content = Text("LoginNewVault");
        RestoreBackup.Content = Text("LoginRestoreBackup");
        LockVault.Content = Text("MenuLockVault").Replace("_", "", StringComparison.Ordinal);
        SearchBox.PlaceholderText = Text("SearchPlaceholder");
        Search.Content = Text("SearchActiveScope");
        InitializeBrowseOptions();
        AddEntry.Content = Text("TbNewEntry");
        EditEntry.Content = Text("TbEdit");
        SetVaultCommandLabels();
        Avalonia.Automation.AutomationProperties.SetName(Entries, Text("AllActiveEntries"));
        Avalonia.Automation.AutomationProperties.SetName(Groups, Text("FoldersAndGroups"));
        PreviousPage.Content = Text("PreviousEntryPage");
        NextPage.Content = Text("NextEntryPage");
        CancelOperation.Content = Text("Cancel");
        if (useNativeSession) _desktopSession = DesktopSession.Create(OnSystemLockStateChanged, settings.ProtectScreenCapture);
        ProtectDialog(this);
        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _idleTimer.Tick += IdleTimer_Tick;
        Opened += (_, _) => MasterPassword.Focus();
        Opened += (_, _) => _idleTimer.Start();
        Closed += (_, _) =>
        {
            _idleTimer.Stop();
            _desktopSession?.Dispose();
        };
    }

    internal void ProtectDialog(Window window)
    {
        _desktopSession?.Protect(window);
        window.AddHandler(KeyDownEvent, (_, _) => ResetIdleTime(), RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(PointerPressedEvent, (_, _) => ResetIdleTime(), RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(PointerWheelChangedEvent, (_, _) => ResetIdleTime(), RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void ResetIdleTime() => _lastInput = Stopwatch.GetTimestamp();

    private async void IdleTimer_Tick(object? sender, EventArgs e)
    {
        if (_closing || _systemLocked) return;
        bool minimized = WindowState == WindowState.Minimized;
        bool lockOnMinimize = minimized && !_minimized && _settings.AutoLockOnMinimize;
        _minimized = minimized;
        bool idle = _settings.AutoLockMinutes > 0 && Stopwatch.GetElapsedTime(_lastInput) >= TimeSpan.FromMinutes(_settings.AutoLockMinutes);
        if ((idle || lockOnMinimize) && (_session != null || _operation != null)) await LockVaultAsync();
    }

    private void OnSystemLockStateChanged(bool locked) => Dispatcher.UIThread.Post(async () =>
    {
        if (_closing) return;
        _systemLocked = locked;
        if (locked && (_session == null || _settings.AutoLockOnWindowsLock)) await LockVaultAsync();
        if (!locked) ResetIdleTime();
        UpdateControls();
    });

    private string Text(string key, params object[] args) => TranslationCatalog.Format(_language, key, CultureInfo.CurrentCulture, args);

    private async void BrowseVault_Click(object? sender, RoutedEventArgs e) => await PickFileAsync(VaultPath, [VaultFileType]);
    private async void BrowseKeyFile_Click(object? sender, RoutedEventArgs e) => await PickFileAsync(KeyFilePath, [FilePickerFileTypes.All]);

    private Task PickFileAsync(TextBox target, IReadOnlyList<FilePickerFileType> filters) => RunOperationAsync(async token =>
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { FileTypeFilter = filters });
        token.ThrowIfCancellationRequested();
        if (files.Count != 0) target.Text = RequireLocalFile(files[0]);
    });

    private static string RequireLocalFile(IStorageFile file) => file.TryGetLocalPath()
        ?? throw new ArgumentException("A local file is required.");

    private async void Unlock_Click(object? sender, RoutedEventArgs e) => await OpenVaultAsync();

    internal Task OpenVaultAsync() => RunOperationAsync(async token =>
    {
        string path = VaultPath.Text?.Trim() ?? "";
        if (!File.Exists(path))
        {
            StatusText.Text = Text("SelectExistingVault");
            return;
        }

        using var master = await ReadCredentialAsync(token);
        var session = await VaultSession.OpenAsync(path, master, _settings,
            () => ConfirmAsync(Text("UpgradeFormatTitle"), Text("UpgradeFormatConfirm")), token);
        if (session != null) await PresentSessionAsync(session, token);
    });

    private async void NewVault_Click(object? sender, RoutedEventArgs e) => await RunOperationAsync(async token =>
    {
        string password = MasterPassword.Text ?? "";
        if (password.Length != 0 && !await ConfirmAsync(Text("ConfirmNewVaultTitle"), Text("ConfirmMasterPassword"), password)) return;
        token.ThrowIfCancellationRequested();
        using var master = await ReadCredentialAsync(token);
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = "PasswordVault.kpd",
            DefaultExtension = "kpd",
            FileTypeChoices = [new FilePickerFileType("SQLitePass") { Patterns = ["*.kpd"] }]
        });
        token.ThrowIfCancellationRequested();
        if (file == null) return;
        string path = RequireLocalFile(file);
        var session = await VaultSession.CreateAsync(path, master, _settings, Text("DefaultGeneralGroup"), Text("RecycleBin"), token);
        VaultPath.Text = path;
        await PresentSessionAsync(session, token);
    });

    private Task<MasterKey> ReadCredentialAsync(CancellationToken token)
    {
        string password = MasterPassword.Text ?? "";
        string? path = KeyFilePath.Text?.Trim();
        MasterPassword.Clear();
        return Task.Run(() => string.IsNullOrEmpty(path) ? new MasterKey(password) : MasterKey.FromFile(password, path), token);
    }

    private async Task PresentSessionAsync(VaultSession session, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            _query = new EntryPageQuery();
            await LoadPageAsync(session, _query, null, token);
            token.ThrowIfCancellationRequested();
            _session = session;
            session.Storage.BackupStatusChanged += OnBackupStatusChanged;
            UpdateBackupStatus();
            ResetIdleTime();
            VaultName.Text = session.Metadata.VaultName;
            UnlockPanel.IsVisible = false;
            VaultPanel.IsVisible = true;
            SearchBox.Focus();
        }
        finally
        {
            if (!ReferenceEquals(_session, session)) await session.DisposeAsync();
        }
    }

    private async void Search_Click(object? sender, RoutedEventArgs e) => await SearchAsync();

    private async void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await SearchAsync();
    }

    internal Task SearchAsync() => RunOperationAsync(async token =>
    {
        if (_session == null) return;
        await RefreshEntryQueryAsync(_session, ReadBrowseOptions(_query), token);
    });

    private async void Group_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Groups.SelectedItem is GroupChoice group) await SelectGroupAsync(group);
    }

    internal Task SelectGroupAsync(GroupChoice group) => RunOperationAsync(async token =>
    {
        if (_session == null) return;
        var query = ReadBrowseOptions(_query with { GroupId = group.Id, OnlyRecycleBin = group.IsRecycleBin });
        try
        {
            await LoadPageAsync(_session, query, null, token);
            _query = query;
        }
        catch
        {
            if (!token.IsCancellationRequested && Groups.ItemsSource is IEnumerable<GroupChoice> choices)
                Groups.SelectedItem = choices.FirstOrDefault(choice => choice.Id == _query.GroupId);
            throw;
        }
    });

    private async void PreviousPage_Click(object? sender, RoutedEventArgs e) => await TurnPageAsync(_previous);
    private async void NextPage_Click(object? sender, RoutedEventArgs e) => await TurnPageAsync(_next);

    internal Task TurnPageAsync(EntryPageCursor? cursor) => RunOperationAsync(async token =>
    {
        if (_session == null || cursor == null) return;
        try { await LoadPageAsync(_session, _query, cursor, token); }
        catch (VaultPageChangedException)
        {
            await RefreshEntryQueryAsync(_session, _query, token);
            StatusText.Text = Text("EntryPageChanged");
        }
    });

    private async Task LoadPageAsync(VaultSession session, EntryPageQuery query, EntryPageCursor? cursor, CancellationToken token)
    {
        var storageQuery = query.GroupId.HasValue && query.GroupId == RootFolderId ? query with { GroupId = null } : query;
        var page = await session.Storage.LoadEntryPageAsync(session.Crypto, storageQuery, cursor, cancellationToken: token);
        try
        {
            token.ThrowIfCancellationRequested();
            SetGroupChoices(page.Groups, query.GroupId);
            Entries.ItemsSource = page.Entries.Select(entry => new EntryRow(entry.Id, entry.Revision, entry.Title, entry.UserName, entry.Url)).ToArray();
            EntryCount.Text = Text("EntriesPageRange", page.FirstIndex, page.Entries.Count == 0 ? 0 : page.FirstIndex + page.Entries.Count - 1, page.MatchedCount);
            _previous = page.PreviousCursor;
            _next = page.NextCursor;
        }
        finally
        {
            foreach (var entry in page.Entries) entry.ClearSecrets();
        }
    }

    private void SetGroupChoices(IReadOnlyList<PwGroup> groups, Guid? selected)
    {
        var map = groups.ToDictionary(group => group.Id);
        string GroupPath(PwGroup group)
        {
            var parts = new Stack<string>();
            parts.Push(group.Name);
            for (Guid? ancestor = group.ParentId; ancestor is { } id && map.TryGetValue(id, out var parent); ancestor = parent.ParentId)
                parts.Push(parent.Name);
            return string.Join(" / ", parts);
        }
        var children = groups.ToLookup(group => group.ParentId);
        var choices = new List<GroupChoice> { new(null, Text("AllActiveEntries")) };
        void AddChildren(Guid? parent)
        {
            foreach (var child in children[parent])
            {
                choices.Add(new GroupChoice(child, GroupPath(child)));
                AddChildren(child.Id);
            }
        }
        AddChildren(null);
        Groups.ItemsSource = choices;
        Groups.SelectedItem = choices.FirstOrDefault(choice => choice.Id == selected) ?? choices[0];
    }

    private void Entries_SelectionChanged(object? sender, SelectionChangedEventArgs e) => UpdateVaultCommands();

    private async void AddEntry_Click(object? sender, RoutedEventArgs e) => await OpenEditorAsync(null);

    private async void EditEntry_Click(object? sender, RoutedEventArgs e)
    {
        if (Entries.SelectedItem is EntryRow row) await OpenEditorAsync(row);
    }

    internal Task OpenEditorAsync(EntryRow? row) => RunOperationAsync(async token =>
    {
        if (_session is not { } session || _query.OnlyRecycleBin) return;
        var source = row == null
            ? new PwEntry { GroupId = _query.GroupId ?? RootFolderId ?? throw new VaultConflictException("A vault root is required.") }
            : await session.Storage.LoadEntryAsync(row.Id, session.Crypto, row.Revision, token);
        EntryEditDraft draft;
        try { draft = await Task.Run(() => EntryEditDraft.Create(source), token); }
        finally { source.ClearSecrets(); }
        using (draft)
        {
            token.ThrowIfCancellationRequested();
            var editor = new EntryEditor(draft.Current, Text, session.Storage.FilePath);
            var saved = await ShowOwnedAsync(editor);
            await editor.WaitForOperationsAsync();
            token.ThrowIfCancellationRequested();
            if (!saved || !await draft.PrepareForSaveAsync(token)) return;
            token.ThrowIfCancellationRequested();
            await session.Storage.SaveEntryAsync(draft.Current, session.Crypto);
        }
        await LoadPageAsync(session, _query, null, token);
    });

    private async void LockVault_Click(object? sender, RoutedEventArgs e) => await LockVaultAsync();

    internal async Task LockVaultAsync()
    {
        var finished = _operationFinished?.Task;
        _operation?.Cancel();
        _ownedDialog?.Close(false);
        var session = _session;
        _session = null;
        VaultPanel.IsVisible = false;
        VaultName.Text = EntryCount.Text = BackupStatus.Text = SearchBox.Text = MasterPassword.Text = KeyFilePath.Text = "";
        Entries.ItemsSource = null;
        Groups.ItemsSource = null;
        ResetBrowseOptions();
        _previous = _next = null;
        if (session != null)
        {
            session.Storage.BackupStatusChanged -= OnBackupStatusChanged;
            await session.DisposeAsync();
        }
        if (finished != null) await finished;
        UnlockPanel.IsVisible = true;
        StatusText.Text = Text("VaultLocked");
        if (!_closing) MasterPassword.Focus();
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_permitClose) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        await LockVaultAsync();
        _permitClose = true;
        // Avoid reentering the native Closing callback when no operation was pending.
        await Task.Yield();
        Close();
    }

    private void CancelOperation_Click(object? sender, RoutedEventArgs e)
    {
        _operation?.Cancel();
        CancelOperation.IsEnabled = false;
    }

    private async Task RunOperationAsync(Func<CancellationToken, Task> action, bool canCancel = false)
    {
        if (_operation != null || _closing || _systemLocked) return;
        using var operation = new CancellationTokenSource();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _operation = operation;
        _operationFinished = finished;
        _canCancelOperation = canCancel;
        UpdateControls();
        StatusText.Text = "";
        try { await action(operation.Token); }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            if (_session != null && !_closing) StatusText.Text = Text("ErrorCancelled");
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Operation did not complete", ex);
            StatusText.Text = Text(ex switch
            {
                UnauthorizedAccessException => "ErrorAccess",
                CryptographicException => "ErrorCredential",
                FileNotFoundException or DirectoryNotFoundException => "ErrorMissingFile",
                InvalidDataException or FormatException => "ErrorIntegrity",
                VaultConflictException => "ErrorConflict",
                SqliteException or IOException => "ErrorStorage",
                ArgumentException => "ErrorInvalidInput",
                _ => "ErrorUnexpected"
            });
        }
        finally
        {
            _operation = null;
            _operationFinished = null;
            _canCancelOperation = false;
            UpdateControls();
            UpdateBackupStatus();
            finished.SetResult();
        }
    }

    private void UpdateControls()
    {
        bool idle = _operation == null && !_closing && !_systemLocked;
        UnlockPanel.IsEnabled = QueryPanel.IsEnabled = GroupPanel.IsEnabled = VaultMenu.IsEnabled = idle;
        CancelOperation.IsVisible = _canCancelOperation && !_closing && !_systemLocked;
        CancelOperation.IsEnabled = _operation?.IsCancellationRequested == false;
        Search.Content = Text(_query.OnlyRecycleBin ? "SearchRecycleBinScope" : "SearchActiveScope");
        UpdateVaultCommands();
        PreviousPage.IsEnabled = idle && _previous != null;
        NextPage.IsEnabled = idle && _next != null;
    }

    private async Task<bool> ConfirmAsync(string title, string message, string? password = null)
    {
        var label = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var contents = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 12, Children = { label } };
        var dialog = new Window
        {
            Title = title, Width = 460, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = contents
        };
        TextBox? confirmation = null;
        var validation = new TextBlock { IsVisible = false, Text = Text("PasswordMismatch") };
        if (password != null)
        {
            confirmation = new TextBox { PasswordChar = '●', IsUndoEnabled = false };
            Avalonia.Automation.AutomationProperties.SetLabeledBy(confirmation, label);
            contents.Children.Add(confirmation);
            contents.Children.Add(validation);
            dialog.Opened += (_, _) => confirmation.Focus();
        }

        var accept = new Button { Content = Text("Ok"), IsDefault = password != null };
        var cancel = new Button { Content = Text("Cancel"), IsCancel = true };
        cancel.Click += (_, _) => dialog.Close(false);
        accept.Click += (_, _) =>
        {
            if (password != null && !string.Equals(password, confirmation?.Text, StringComparison.Ordinal))
            {
                validation.IsVisible = true;
                return;
            }
            dialog.Close(true);
        };
        contents.Children.Add(new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children = { accept, cancel } });
        try { return await ShowOwnedAsync(dialog); }
        finally { confirmation?.Clear(); }
    }

    private async Task<bool> ShowOwnedAsync(Window dialog)
    {
        ProtectDialog(dialog);
        _ownedDialog = dialog;
        try { return await dialog.ShowDialog<bool>(this); }
        finally { _ownedDialog = null; }
    }
}
