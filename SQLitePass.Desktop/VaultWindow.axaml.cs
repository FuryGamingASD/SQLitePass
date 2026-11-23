using System.ComponentModel;
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
using SQLitePass.UI.Controls;
using SQLitePass.UI.Services;

namespace SQLitePass.Desktop;

internal sealed class EntryRow(Guid id, long revision, string title, string userName, bool hasPassword,
    string url, string notes, string updated, double? passwordQuality) : INotifyPropertyChanged
{
    private string? _revealedPassword;

    public Guid Id { get; } = id;
    public long Revision { get; } = revision;
    public string Title { get; } = title;
    public string UserName { get; } = userName;
    public bool HasPassword { get; } = hasPassword;
    public string Url { get; } = url;
    public string Notes { get; } = notes;
    public string Updated { get; } = updated;
    public double? PasswordQuality { get; } = passwordQuality;

    public event PropertyChangedEventHandler? PropertyChanged;
    public AppIconKind Icon => AppIconLabels.Entry(Title);
    public string PasswordMask => HasPassword ? "••••••••" : "";
    public string DisplayPassword => _revealedPassword ?? PasswordMask;

    public void SetRevealedPassword(string? password)
    {
        if (_revealedPassword == password) return;
        _revealedPassword = password;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayPassword)));
    }
}
internal sealed class GroupChoice(PwGroup? folder, string name)
{
    public PwGroup? Folder { get; } = folder;
    public string Name { get; } = name;
    public Guid? Id => Folder?.Id;
    public bool IsRecycleBin => Folder?.IsRecycleBin == true;
    public string Caption => Folder?.Name ?? Name;
    public AppIconKind Icon => IsRecycleBin ? AppIconKind.Trash :
        Folder?.IsRoot == true ? AppIconKind.Shield : AppIconLabels.Group(Caption);
    public IReadOnlyList<GroupChoice> Children { get; set; } = [];
    public Action<Guid, bool>? ExpansionChanged { get; set; }
    private bool _expanded;

    public bool Expanded
    {
        get => _expanded;
        set
        {
            if (_expanded == value) return;
            _expanded = value;
            if (Id is { } id) ExpansionChanged?.Invoke(id, value);
        }
    }

    public override string ToString() => Name;
}

public sealed partial class VaultWindow : Window
{
    private static readonly FilePickerFileType VaultFileType = new("SQLitePass") { Patterns = ["*.kpd", "*.db"] };
    private AppSettings _settings;
    private readonly string _language;
    private readonly DispatcherTimer _idleTimer;
    private readonly IDesktopSession? _desktopSession;
    private VaultSession? _session;
    private CancellationTokenSource? _operation;
    private TaskCompletionSource? _operationFinished;
    private Window? _ownedDialog;
    private EntryPageQuery _query = new();
    private EntryPageCursor? _shownPage;
    private EntryPageCursor? _previous;
    private EntryPageCursor? _next;
    private bool _closing;
    private bool _permitClose;
    private bool _minimized;
    private bool _systemLocked;
    private bool _canCancelOperation;
    private long _lastInput = Stopwatch.GetTimestamp();

    public VaultWindow() : this(new AppSettings()) { }

    public VaultWindow(AppSettings settings, bool useNativeSession = false) : this(settings, useNativeSession, null) { }

    internal VaultWindow(AppSettings settings, bool useNativeSession, SessionClipboard? clipboard)
    {
        _clipboard = clipboard;
        _settings = settings;
        _language = TranslationCatalog.ResolveLanguage(settings.Language, CultureInfo.CurrentUICulture);
        InitializeComponent();
        Topmost = settings.AlwaysOnTop;

        VaultPathLabel.Text = Text("LoginDatabaseFile");
        VaultPath.Text = settings.LastOpenedVaultPath;
        BrowseVault.Content = BrowseKeyFile.Content = Text("Browse");
        PasswordLabel.Text = Text("LoginMasterPassword");
        KeyFileLabel.Text = Text("LoginUseKeyFile");
        Unlock.Content = Text("LoginUnlock");
        NewVault.Content = Text("LoginNewVault");
        RestoreBackup.Content = Text("LoginRestoreBackup");
        LoginGenerator.Content = Text("GeneratorTitle");
        LoginCheckUpdates.Content = Text("MenuCheckUpdates").Replace("_", "", StringComparison.Ordinal);
        SearchBox.PlaceholderText = Text("SearchPlaceholder");
        InitializeBrowseOptions();
        SetVaultCommandLabels();
        InitializeWorkspace(useNativeSession);
        Avalonia.Automation.AutomationProperties.SetName(Entries, Text("EntriesList"));
        Avalonia.Automation.AutomationProperties.SetName(Groups, Text("FoldersAndGroups"));
        PreviousPage.Content = Text("PreviousEntryPage");
        NextPage.Content = Text("NextEntryPage");
        CancelOperation.Content = Text("Cancel");
        if (useNativeSession) _desktopSession = DesktopSession.Create(OnSystemLockStateChanged);
        ProtectDialog(this);
        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _idleTimer.Tick += IdleTimer_Tick;
        Opened += (_, _) => MasterPassword.Focus();
        Opened += (_, _) => _idleTimer.Start();
        if (useNativeSession) Opened += (_, _) =>
        {
            if (_nativeIntegrationsStarted) return;
            _nativeIntegrationsStarted = true;
            InitializeClipboard();
            InitializeTray();
            InitializeAutoType();
        };
        Closed += (_, _) =>
        {
            _idleTimer.Stop();
            _desktopSession?.Dispose();
            _clipboard?.Dispose();
            _tray?.Dispose();
        };
    }

    internal void ProtectDialog(Window window)
    {
        _desktopSession?.Protect(window, _settings.ProtectScreenCapture);
        window.AddHandler(KeyDownEvent, (_, _) => ResetIdleTime(), RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(PointerPressedEvent, (_, _) => ResetIdleTime(), RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(PointerWheelChangedEvent, (_, _) => ResetIdleTime(), RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void ResetIdleTime() => _lastInput = Stopwatch.GetTimestamp();

    private async void IdleTimer_Tick(object? sender, EventArgs e) => await CheckDesktopIdleAsync(Stopwatch.GetElapsedTime(_lastInput));

    internal async Task CheckDesktopIdleAsync(TimeSpan elapsed)
    {
        if (_closing || _systemLocked) return;
        bool minimized = WindowState == WindowState.Minimized;
        bool justMinimized = minimized && !_minimized;
        bool lockOnMinimize = justMinimized && _settings.AutoLockOnMinimize;
        _minimized = minimized;
        bool idle = _settings.AutoLockMinutes > 0 && elapsed >= TimeSpan.FromMinutes(_settings.AutoLockMinutes);
        if ((idle || lockOnMinimize) && (_session != null || _operation != null)) await LockVaultAsync();
        if (justMinimized && _settings.MinimizeToTray && WindowState == WindowState.Minimized) HideInTray();
    }

    private void OnSystemLockStateChanged(bool locked) => Dispatcher.UIThread.Post(async () =>
    {
        if (_closing) return;
        _systemLocked = locked;
        if (locked) StopAutoType();
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
        HideLoginPassword();
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
            RememberVaultPath();
            UnlockPanel.IsVisible = false;
            VaultPanel.IsVisible = true;
            EnterVaultLayout();
            RegisterGlobalAutoType();
            SearchBox.Focus();
            if (_settings.ShowPasswordColumnDefault) await RevealPasswordColumnPageAsync(token);
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
        _searchTimer.Stop();
        if (_session == null) return;
        await RefreshEntryQueryAsync(_session, ReadBrowseOptions(_query), token);
    });

    private async void Group_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Groups.SelectedItem is not GroupChoice group) return;
        // Loading the selected folder replaces the group rows, including the pointer's drag source.
        if (_pendingWorkspaceDrag?.Row is PwGroup pressed && pressed.Id == group.Id) return;
        await SelectGroupAsync(group);
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
            if (!token.IsCancellationRequested)
                Groups.SelectedItem = FolderChoices.FirstOrDefault(choice => choice.Id == _query.GroupId);
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
            SetGroupChoices(page.Groups, query.GroupId, session.Metadata.VaultId);
            HideColumnPasswords();
            var selectedEntry = (Entries.SelectedItem as EntryRow)?.Id;
            var rows = page.Entries.Select(entry => new EntryRow(entry.Id, entry.Revision, entry.Title, entry.UserName,
                !string.IsNullOrEmpty(entry.Password), entry.Url, NotePreview(entry.Notes),
                entry.UpdatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                entry.Password.Length is > 0 and <= 512 ? PasswordGenerator.CalculateEntropy(entry.Password) : null)).ToArray();
            Entries.ItemsSource = rows;
            Entries.SelectedItem = rows.FirstOrDefault(row => row.Id == selectedEntry);
            EmptyEntryPanel.IsVisible = rows.Length == 0;
            UpdateEntryPreview();
            EntryCount.Text = Text("EntriesPageRange", page.FirstIndex, page.Entries.Count == 0 ? 0 : page.FirstIndex + page.Entries.Count - 1, page.MatchedCount);
            _shownPage = cursor;
            _previous = page.PreviousCursor;
            _next = page.NextCursor;
        }
        finally
        {
            foreach (var entry in page.Entries) entry.ClearSecrets();
        }
    }

    private void Entries_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateEntryPreview();
        UpdateVaultCommands();
    }

    private async void AddEntry_Click(object? sender, RoutedEventArgs e) => await OpenEditorAsync(null);

    private async void EditEntry_Click(object? sender, RoutedEventArgs e)
    {
        if (Entries.SelectedItem is EntryRow row) await OpenSelectedEntryAsync(row);
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
            var editor = new EntryEditor(draft.Current, Text, session.Storage.FilePath,
                () => CreatePasswordGenerator(token, allowUse: true));
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

    internal async Task<bool> LockAfterUnexpectedErrorAsync()
    {
        bool hadVault = _session != null && !_closing;
        await LockVaultAsync();
        if (hadVault) StatusText.Text = Text("ErrorUnexpected");
        return hadVault;
    }

    internal async Task LockVaultAsync()
    {
        _pendingWorkspaceDrag = null;
        SetDropIndicator(null, null);
        HidePasswordReveals();
        _searchTimer.Stop();
        _autoTypeHotkey?.Dispose();
        _autoTypeHotkey = null;
        var finished = _operationFinished?.Task;
        _operation?.Cancel();
        if (_ownedDialog is EntryEditor editor) editor.CloseForVaultLock();
        else _ownedDialog?.Close(false);
        var session = _session;
        _session = null;
        VaultPanel.IsVisible = false;
        ClipboardStatus.Text = "";
        FolderPath.Text = EntryCount.Text = BackupStatus.Text = SearchBox.Text = MasterPassword.Text = KeyFilePath.Text = "";
        UseKeyFile.IsChecked = false;
        Entries.ItemsSource = null;
        Groups.ItemsSource = null;
        FolderChoices = [];
        UpdateEntryPreview();
        ResetBrowseOptions();
        _shownPage = _previous = _next = null;
        var clipboardClear = _clipboard?.ClearAsync();
        if (session != null)
        {
            session.Storage.BackupStatusChanged -= OnBackupStatusChanged;
            await session.DisposeAsync();
        }
        if (finished != null) await finished;
        var clipboardResult = clipboardClear == null ? ClipboardClearResult.Replaced : await clipboardClear;
        UnlockPanel.IsVisible = true;
        StatusText.Text = clipboardResult == ClipboardClearResult.Busy ? Text("VaultLocked") + " · " + Text("ClipboardClearFailed") : Text("VaultLocked");
        if (!_closing)
        {
            await LeaveVaultLayoutAsync();
            MasterPassword.Focus();
        }
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_permitClose) return;
        if (e.CloseReason == WindowCloseReason.WindowClosing && !_exitRequested && _settings.CloseToTray && _tray?.CanHide == true)
        {
            e.Cancel = true;
            if (_settings.AutoLockOnMinimize) await LockVaultAsync();
            if (HideInTray()) return;
            // The tray host may have disappeared while the vault was locking.
            _exitRequested = true;
            await Task.Yield();
            Close();
            return;
        }
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        await LockVaultAsync();
        await FlushWorkspacePreferencesAsync();
        await SaveWindowSizeAsync(this);
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
        UnlockPanel.IsEnabled = GroupPanel.IsEnabled = VaultMenu.IsEnabled = VaultToolbar.IsEnabled = Entries.IsEnabled = idle;
        QueryPanel.IsEnabled = idle || _liveSearch && !_closing && !_systemLocked;
        EntryPreview.IsEnabled = idle;
        CancelOperation.IsVisible = _canCancelOperation && !_closing && !_systemLocked;
        CancelOperation.IsEnabled = _operation?.IsCancellationRequested == false;
        LabelAction(Search, _query.OnlyRecycleBin ? "SearchRecycleBinScope" : "SearchActiveScope");
        UpdateVaultCommands();
        PreviousPage.IsEnabled = idle && _previous != null;
        NextPage.IsEnabled = idle && _next != null;
    }

    private async Task<bool> ConfirmAsync(string title, string message, string? password = null, string? acceptLabel = null)
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

        var accept = new Button { Content = acceptLabel ?? Text("Ok"), IsDefault = password != null };
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
        PrepareWindowSize(dialog);
        _ownedDialog = dialog;
        try { return await dialog.ShowDialog<bool>(this); }
        finally
        {
            _ownedDialog = null;
            await SaveWindowSizeAsync(dialog);
        }
    }
}
