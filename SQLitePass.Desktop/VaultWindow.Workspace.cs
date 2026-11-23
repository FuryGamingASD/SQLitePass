using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;
using SQLitePass.UI.Controls;

namespace SQLitePass.Desktop;

public sealed partial class VaultWindow
{
    private readonly DispatcherTimer _loginRevealTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly DispatcherTimer _previewRevealTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly DispatcherTimer _backupBadgeTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _expansionTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly Dictionary<string, bool> _pendingExpansion = new();
    private Task _expansionSave = Task.CompletedTask;
    private bool _saveWorkspacePreferences;
    private readonly DispatcherTimer _columnRevealTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private int _columnRevealRequest;
    private int _previewRequest;
    private int _backupBadgeRequest;

    private void InitializeWorkspace(bool persist)
    {
        _saveWorkspacePreferences = persist;
        LoginSubtitle.Text = Text("LoginHeaderSubtitle");
        LoginBackupFound.Text = Text("LoginBackupPresent");
        LoginSettingsLabel.Text = VaultSettingsLabel.Text = Text("TbSettings");
        LoginExit.Content = Text("MenuExit").Replace("_", "");
        FolderHeading.Text = Text("FoldersAndGroups");
        AddEntryLabel.Text = Text("TbNewEntry");
        TitleColumn.Text = Text("ColTitle");
        UserColumn.Text = Text("ColUserName");
        PasswordColumn.Text = Text("ColPassword");
        UrlColumn.Text = Text("ColUrl");
        NotesColumn.Text = Text("ColNotes");
        UpdatedColumn.Text = Text("ColUpdatedAt");
        EmptyEntries.Text = Text("NoEntriesInView");
        EmptyNewEntry.Content = Text("TbNewEntry");
        foreach (var (control, key) in new (Control, string)[]
        {
            (LoginTheme, "MenuToggleTheme"), (VaultTheme, "MenuToggleTheme"), (RecentVaultButton, "RecentVaults"),
            (LoginReveal, "ShowPassword"), (PreviewReveal, "ShowPassword"), (PreviewCopyPassword, "TbCopyPassword"),
            (PreviewEstimateQuality, "EstimatePasswordQuality"),
            (LockVault, "TbLock"), (AddEntry, "TbNewEntry"), (EditEntry, "TbEdit"), (ToolbarDelete, "TbDelete"),
            (ToolbarCopyUser, "TbCopyUser"), (ToolbarCopyPassword, "TbCopyPassword"), (ToolbarOpenUrl, "TbOpenUrl"),
            (ToolbarAutoType, "TbAutoType"), (ToolbarGenerator, "TbGenerator"), (SidebarAddFolder, "NewGroup"),
            (LoginSettings, "TbSettings"), (VaultSettings, "TbSettings")
        }) LabelAction(control, key);

        ApplyWorkspaceTheme(_settings.DarkMode);
        if (Application.Current?.PlatformSettings is { } platform)
        {
            platform.ColorValuesChanged += PlatformColorsChanged;
            Closed += (_, _) => platform.ColorValuesChanged -= PlatformColorsChanged;
        }
        ApplyLoginLayout();
        InitializeWindowControls();
        UpdateRecentVaults();
        PropertyChanged += (_, change) =>
        {
            if (change.Property == BoundsProperty) Classes.Set("wide", Bounds.Width >= 1000);
        };
        Deactivated += (_, _) => HidePasswordReveals();
        _loginRevealTimer.Tick += (_, _) => HideLoginPassword();
        _previewRevealTimer.Tick += (_, _) => HidePreviewPassword();
        _columnRevealTimer.Tick += (_, _) => HideColumnPasswords();
        AddHandler(KeyDownEvent, Workspace_KeyDown, RoutingStrategies.Tunnel);
        InitializeWorkspaceDrag();
        _backupBadgeTimer.Tick += BackupBadgeTimer_Tick;
        _expansionTimer.Tick += SaveExpansion_Tick;
        MasterPassword.AddHandler(TextBox.CopyingToClipboardEvent, (_, args) => args.Handled = true);
        MasterPassword.AddHandler(TextBox.CuttingToClipboardEvent, (_, args) => args.Handled = true);

        Entries.ContextMenu = ContextActions(NewEntryCommand, EditEntryCommand, CopyUser, CopyPassword, OpenUrlCommand,
            AutoTypeEntry, DuplicateEntry, MoveEntryCommand, RestoreEntry, DeleteEntry);
        Groups.ContextMenu = ContextActions(NewEntryCommand, CreateFolder, RenameFolder, MoveFolder, MoveFolderToRoot,
            MoveFolderUp, MoveFolderDown, DeleteFolder, EmptyRecycleBin);
        QueueBackupBadge();
    }

    private void LabelAction(Control control, string key)
    {
        string title = Text(key).Replace("_", "");
        AutomationProperties.SetName(control, title);
        ToolTip.SetTip(control, title);
    }

    private static ContextMenu ContextActions(params MenuItem[] commands)
    {
        var menu = new ContextMenu();
        foreach (var command in commands)
        {
            var item = new MenuItem { Header = command.Header };
            item.Bind(IsEnabledProperty, new Binding(nameof(IsEnabled)) { Source = command });
            item.Click += (_, _) => command.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.Items.Add(item);
        }
        return menu;
    }

    private void ApplyWorkspaceTheme(bool dark)
    {
        var theme = Application.Current?.PlatformSettings?.GetColorValues().ContrastPreference == ColorContrastPreference.High
            ? DesktopAppearance.HighContrast : dark ? ThemeVariant.Dark : ThemeVariant.Light;
        if (Application.Current is { } app) app.RequestedThemeVariant = theme;
        LoginThemeIcon.Kind = VaultThemeIcon.Kind = dark ? AppIconKind.Sun : AppIconKind.Theme;
    }

    private void PlatformColorsChanged(object? sender, PlatformColorValues options) =>
        Dispatcher.UIThread.Post(() => { if (!_closing) ApplyWorkspaceTheme(_settings.DarkMode); });

    private async void ToggleTheme_Click(object? sender, RoutedEventArgs e) =>
        await ToggleThemeAsync(dark => AppSettingsManager.Update(settings => settings.DarkMode = dark));

    internal Task ToggleThemeAsync(Func<bool, AppSettings> save) => RunOperationAsync(async token =>
    {
        bool dark = !_settings.DarkMode;
        try
        {
            var saved = await Task.Run(() => save(dark), token);
            _settings.DarkMode = saved.DarkMode;
            ApplyWorkspaceTheme(saved.DarkMode);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            AppLogger.LogError("Could not save the theme", error);
            StatusText.Text = Text("ThemeSaveFailed");
        }
    });

    private void SetGroupChoices(IReadOnlyList<PwGroup> groups, Guid? selected, Guid vaultId)
    {
        var previous = FolderChoices.Where(choice => choice.Id != null).ToDictionary(choice => choice.Id!.Value, choice => choice.Expanded);
        var children = groups.ToLookup(group => group.ParentId);
        var all = new GroupChoice(null, Text("AllActiveEntries"));
        var choices = new List<GroupChoice> { all };
        var roots = new List<GroupChoice> { all };
        foreach (var group in children[null]) roots.Add(Build(group, null));
        FolderChoices = choices;
        Groups.ItemsSource = roots;
        var selection = choices.FirstOrDefault(choice => choice.Id == selected) ?? all;
        Groups.SelectedItem = selection;
        FolderPath.Text = selection.Name;

        GroupChoice Build(PwGroup group, string? parentPath)
        {
            string name = parentPath == null ? group.Name : parentPath + " / " + group.Name;
            var path = $"{vaultId:N}/{group.Id:N}";
            bool expanded = previous.TryGetValue(group.Id, out bool current) ? current :
                _settings.GroupExpansion.GetValueOrDefault(path, group.IsRoot || group.IsExpanded);
            var choice = new GroupChoice(group, name) { Expanded = expanded };
            choices.Add(choice);
            choice.Children = children[group.Id].Select(child => Build(child, name)).ToArray();
            choice.ExpansionChanged = (id, value) => QueueExpansion(vaultId, id, value);
            return choice;
        }
    }

    private void QueueExpansion(Guid vault, Guid group, bool expanded)
    {
        if (_session?.Metadata.VaultId != vault || _closing) return;
        string key = $"{vault:N}/{group:N}";
        _settings.GroupExpansion[key] = expanded;
        if (!_saveWorkspacePreferences) return;
        _pendingExpansion[key] = expanded;
        _expansionTimer.Stop();
        _expansionTimer.Start();
    }

    private async void SaveExpansion_Tick(object? sender, EventArgs e)
    {
        _expansionTimer.Stop();
        if (!_expansionSave.IsCompleted) { _expansionTimer.Start(); return; }
        await SaveExpansionAsync();
    }

    private async Task SaveExpansionAsync()
    {
        if (_pendingExpansion.Count == 0) return;
        var changes = new Dictionary<string, bool>(_pendingExpansion);
        try
        {
            _expansionSave = Task.Run(() => AppSettingsManager.Update(settings =>
            {
                foreach (var (key, value) in changes) settings.GroupExpansion[key] = value;
            }));
            await _expansionSave;
            foreach (var (key, value) in changes)
                if (_pendingExpansion.TryGetValue(key, out bool latest) && latest == value) _pendingExpansion.Remove(key);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            AppLogger.LogError("Could not save folder expansion", error);
            if (!_closing && _session != null) StatusText.Text = Text("FolderStateSaveWarning");
        }
    }

    private async Task FlushWorkspacePreferencesAsync()
    {
        _expansionTimer.Stop();
        try { await _expansionSave; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            AppLogger.LogError("Could not finish saving folder expansion", error);
        }
        await SaveExpansionAsync();
    }

    private void UpdateRecentVaults()
    {
        RecentVaults.ItemsSource = _settings.RecentVaults.ToArray();
        RecentVaultButton.IsEnabled = _settings.RecentVaults.Count != 0;
    }

    private void RememberVaultPath()
    {
        if (VaultPath.Text?.Trim() is not { Length: > 0 } path) return;
        _settings.RecentVaults.RemoveAll(current => string.Equals(current, path,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        _settings.RecentVaults.Insert(0, path);
        if (_settings.RecentVaults.Count > 10) _settings.RecentVaults.RemoveAt(10);
        UpdateRecentVaults();
        if (_saveWorkspacePreferences) _ = PersistRecentVaultAsync(path);
    }

    private static async Task PersistRecentVaultAsync(string path)
    {
        try { await Task.Run(() => AppSettingsManager.AddRecentVault(path)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            AppLogger.LogError("Could not save the recently opened vault", error);
        }
    }

    private void RecentVault_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (RecentVaults.SelectedItem is not string path) return;
        VaultPath.Text = path;
        RecentVaultButton.Flyout?.Hide();
        RecentVaults.SelectedItem = null;
        MasterPassword.Focus();
    }

    private void UseKeyFile_Changed(object? sender, RoutedEventArgs e)
    {
        if (KeyFilePanel == null) return;
        KeyFilePanel.IsVisible = UseKeyFile.IsChecked == true;
        if (!KeyFilePanel.IsVisible) KeyFilePath.Clear();
    }

    private void KeyFilePath_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!string.IsNullOrEmpty(KeyFilePath.Text)) UseKeyFile.IsChecked = true;
    }

    private void VaultPath_TextChanged(object? sender, TextChangedEventArgs e) => QueueBackupBadge();

    private void QueueBackupBadge()
    {
        ++_backupBadgeRequest;
        if (LoginBackupFound == null) return;
        LoginBackupFound.IsVisible = false;
        _backupBadgeTimer.Stop();
        if (!string.IsNullOrWhiteSpace(VaultPath.Text)) _backupBadgeTimer.Start();
    }

    private async void BackupBadgeTimer_Tick(object? sender, EventArgs e)
    {
        _backupBadgeTimer.Stop();
        int request = _backupBadgeRequest;
        string backup = VaultPath.Text?.Trim() + ".backup";
        bool found = await Task.Run(() => File.Exists(backup));
        if (request == _backupBadgeRequest && !_closing) LoginBackupFound.IsVisible = found;
    }

    private void LoginReveal_Click(object? sender, RoutedEventArgs e)
    {
        bool reveal = !MasterPassword.RevealPassword && !string.IsNullOrEmpty(MasterPassword.Text);
        HideLoginPassword();
        if (!reveal) return;
        MasterPassword.RevealPassword = true;
        LoginRevealIcon.Kind = AppIconKind.EyeOff;
        LabelAction(LoginReveal, "HidePassword");
        _loginRevealTimer.Start();
    }

    private void HideLoginPassword()
    {
        _loginRevealTimer.Stop();
        MasterPassword.RevealPassword = false;
        LoginRevealIcon.Kind = AppIconKind.Eye;
        LabelAction(LoginReveal, "ShowPassword");
    }

    private void HidePasswordReveals()
    {
        HideLoginPassword();
        HidePreviewPassword();
        HideColumnPasswords();
    }

    private async void TogglePasswordColumn_Click(object? sender, RoutedEventArgs e)
    {
        if (_columnRevealTimer.IsEnabled)
        {
            HideColumnPasswords();
            return;
        }
        await RunOperationAsync(RevealPasswordColumnPageAsync);
    }

    private async Task RevealPasswordColumnPageAsync(CancellationToken token)
    {
        if (_session is not { } session || _query.OnlyRecycleBin || _shownPageStamp is not { } stamp) return;
        var rows = Entries.Items.OfType<EntryRow>().Where(row => row.HasPassword).ToDictionary(row => row.Id);
        if (rows.Count == 0) return;
        int request = ++_columnRevealRequest;
        var revisions = rows.ToDictionary(pair => pair.Key, pair => pair.Value.Revision);
        var passwords = await session.Storage.LoadPasswordColumnAsync(session.Crypto, stamp, revisions, token);
        token.ThrowIfCancellationRequested();
        if (request != _columnRevealRequest || !ReferenceEquals(_session, session)) return;
        foreach (var (id, password) in passwords) rows[id].SetRevealedPassword(password);
        _columnRevealTimer.Start();
    }

    private void HideColumnPasswords()
    {
        ++_columnRevealRequest;
        _columnRevealTimer.Stop();
        foreach (var row in Entries.Items.OfType<EntryRow>()) row.SetRevealedPassword(null);
    }

    private void Groups_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None) return;
        var action = e.Key switch { Key.F2 => RenameFolder, Key.Delete => DeleteFolder, _ => null };
        InvokeShortcut(action, e);
    }

    private void Workspace_KeyDown(object? sender, KeyEventArgs e)
    {
        if (!VaultPanel.IsVisible || _operation != null || _systemLocked) return;
        if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.F5)
        {
            InvokeShortcut(RefreshCommand, e);
            return;
        }
        if (e.KeyModifiers != KeyModifiers.Control) return;
        bool textInput = e.Source is TextBox || FocusManager?.GetFocusedElement() is TextBox;
        var action = e.Key switch
        {
            Key.I => NewEntryCommand,
            Key.G => NewFolderCommand,
            Key.L => LockCommand,
            Key.M => ChangeCredential,
            Key.W => PasswordGeneratorCommand,
            Key.H => PasswordColumnCommand,
            Key.Up when !textInput => MoveFolderUp,
            Key.Down when !textInput => MoveFolderDown,
            Key.V when !textInput => AutoTypeEntry,
            _ => null
        };
        InvokeShortcut(action, e);
    }

    private static void InvokeShortcut(MenuItem? action, KeyEventArgs e)
    {
        if (action?.IsEnabled != true) return;
        e.Handled = true;
        action.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }

    private async void About_Click(object? sender, RoutedEventArgs e) => await RunOperationAsync(async _ =>
    {
        string version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "";
        await ConfirmAsync(Text("MenuAbout").Replace("_", ""), $"SQLitePass {version}");
    });
}
