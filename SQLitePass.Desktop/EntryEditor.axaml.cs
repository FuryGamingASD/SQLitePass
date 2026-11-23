using Avalonia.Controls;
using Avalonia.Interactivity;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;

namespace SQLitePass.Desktop;

public sealed partial class EntryEditor : Window
{
    private readonly EntryEditDraft _editDraft;
    private PwEntry _draft => _editDraft.Current;
    private readonly Func<string, object[], string> _text;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly string? _vaultPath;
    private readonly Func<PasswordGeneratorDialog>? _createGenerator;
    private Window? _confirmation;
    private Task _operation = Task.CompletedTask;
    private readonly bool _readOnly;
    private bool _closed;
    private bool _busy;
    private bool _edited;
    private bool _permitClose;

    public EntryEditor() : this(EntryEditDraft.Create(new PwEntry()), (key, _) => key) { }

    internal EntryEditor(EntryEditDraft draft, Func<string, object[], string> text, string? vaultPath = null,
        Func<PasswordGeneratorDialog>? createGenerator = null, bool readOnly = false)
    {
        _editDraft = draft;
        _text = text;
        _vaultPath = vaultPath;
        _createGenerator = createGenerator;
        _readOnly = readOnly;
        InitializeComponent();
        Title = text(readOnly ? "EntryViewTitle" : _draft.Revision > 0 ? "EntryEditTitle" : "EntryNewTitle", []);
        TitleLabel.Text = text("LblTitle", []);
        UserLabel.Text = text("LblUserName", []);
        PasswordLabel.Text = text("LblPassword", []);
        GeneratePassword.Content = text("TbGenerator", []);
        UrlLabel.Text = text("LblUrl", []);
        TagsLabel.Text = text("LblTags", []);
        NotesLabel.Text = text("LblNotes", []);
        ValidationMessage.Text = text("TitleRequired", []);
        CredentialsTab.Header = text("TabCredentials", []);
        EnableExpiry.Content = text("ChkEnableExpiry", []);
        CustomFieldsTab.Header = text("CustomFieldsTab", []);
        AttachmentsTab.Header = text("AttachmentsTab", []);
        HistoryTab.Header = text("TabHistory", []);
        AutoTypeTab.Header = text("AutoTypeTab", []);
        AutoTypeEnabled.Content = text("AutoTypeEnabled", []);
        SequenceLabel.Text = text("AutoTypeSequence", []);
        Cancel.Content = text("Cancel", []);
        Save.Content = text("Save", []);

        SetCredentialFields(_draft);
        InitializeCustomFields();
        InitializeAttachments();
        InitializeHistory();
        InitializeSensitiveReveals();
        Sections.SelectionChanged += LoadSelectedDetails;
        if (readOnly)
        {
            foreach (var field in new[] { EntryTitle, UserName, Password, Url, Tags, Notes, FieldName, FieldValue, Sequence })
                field.IsReadOnly = true;
            foreach (var command in new[] { Save, GeneratePassword, AddField, SaveField, RemoveField, AddAttachment,
                RemoveAttachment, RestoreHistory, ClearHistory })
                command.IsVisible = false;
            EnableExpiry.IsEnabled = Expiry.IsEnabled = FieldProtected.IsEnabled = AutoTypeEnabled.IsEnabled = false;
            Cancel.Content = text("Close", []);
        }
        EditedIndicator.Text = text("EntryUnsavedChanges", []);
        foreach (var field in new[] { EntryTitle, UserName, Password, Url, Tags, Notes, Sequence })
            field.TextChanged += (_, _) => MarkCredentialChanges();
        EnableExpiry.IsCheckedChanged += (_, _) => MarkCredentialChanges();
        Expiry.SelectedDateChanged += (_, _) => MarkCredentialChanges();
        AutoTypeEnabled.IsCheckedChanged += (_, _) => MarkCredentialChanges();
        Opened += (_, _) => EntryTitle.Focus();
        Closed += (_, _) =>
        {
            _closed = true;
            _lifetime.Cancel();
            HideSensitiveValue();
            if (_confirmation?.IsVisible == true) _confirmation.Close(false);
            CustomFields.ItemsSource = Attachments.ItemsSource = History.ItemsSource = null;
            EntryTitle.Clear(); UserName.Clear(); Password.Clear(); Url.Clear(); Tags.Clear(); Notes.Clear(); Sequence.Clear();
            FieldName.Clear(); FieldValue.Clear();
            _lifetime.Dispose();
        };
    }

    private void SetCredentialFields(PwEntry entry)
    {
        EntryTitle.Text = entry.Title;
        UserName.Text = entry.UserName;
        Password.Text = entry.Password;
        Url.Text = entry.Url;
        Tags.Text = entry.Tags;
        Notes.Text = entry.Notes;
        EnableExpiry.IsChecked = entry.ExpiryDate.HasValue;
        Expiry.SelectedDate = entry.ExpiryDate is { } expires ? new DateTimeOffset(expires.Date.Ticks, TimeSpan.Zero) : null;
        Expiry.IsEnabled = EnableExpiry.IsChecked == true;
        AutoTypeEnabled.IsChecked = entry.AutoTypeEnabled;
        Sequence.Text = entry.AutoTypeSequence;
    }

    private void EnableExpiry_Changed(object? sender, RoutedEventArgs e)
    {
        if (Expiry == null) return;
        Expiry.IsEnabled = EnableExpiry.IsChecked == true;
        if (Expiry.IsEnabled && Expiry.SelectedDate == null) Expiry.SelectedDate = DateTimeOffset.Now.Date.AddMonths(3);
    }

    private void MarkEdited()
    {
        if (_readOnly || _closed) return;
        _edited = EditedIndicator.IsVisible = true;
    }

    private void MarkCredentialChanges()
    {
        DateTime? expiry = EnableExpiry.IsChecked == true ? Expiry.SelectedDate?.Date : null;
        if ((EntryTitle.Text ?? "") != _draft.Title || (UserName.Text ?? "") != _draft.UserName ||
            (Password.Text ?? "") != _draft.Password || (Url.Text ?? "") != _draft.Url ||
            (Tags.Text ?? "") != _draft.Tags || (Notes.Text ?? "") != _draft.Notes ||
            (Sequence.Text ?? "") != _draft.AutoTypeSequence || expiry != _draft.ExpiryDate?.Date ||
            (AutoTypeEnabled.IsChecked == true) != _draft.AutoTypeEnabled)
            MarkEdited();
    }

    internal void CloseForVaultLock()
    {
        _permitClose = true;
        if (!_closed) Close(false);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => CloseForVaultLock();

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_permitClose || _readOnly || e.Cancel || e.CloseReason != WindowCloseReason.WindowClosing ||
            (!_edited && !HasUncommittedField())) return;
        e.Cancel = true;
        if (_confirmation != null) return;
        HideSensitiveValue();
        try
        {
            if (await ConfirmHistoryAsync(_text("EntryUnsavedChanges", []), _text("EntryDiscardChanges", []), _lifetime.Token) && !_closed)
            {
                await Task.Yield();
                CloseForVaultLock();
            }
        }
        catch (OperationCanceledException) when (_closed) { }
    }

    private async void GeneratePassword_Click(object? sender, RoutedEventArgs e)
    {
        if (_closed || _busy || _confirmation != null) return;
        var dialog = _createGenerator?.Invoke() ?? new PasswordGeneratorDialog(_text, null, allowUse: true);
        _confirmation = dialog;
        try
        {
            if (!await dialog.ShowDialog<bool>(this)) return;
            string password = dialog.TakePassword();
            if (_closed) return;
            HideSensitiveValue();
            Password.Text = password;
            Password.Focus();
        }
        finally { _confirmation = null; }
    }

    internal void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (_readOnly || _closed || _busy) return;
        if (string.IsNullOrWhiteSpace(EntryTitle.Text))
        {
            ShowValidation("TitleRequired");
            Sections.SelectedItem = CredentialsTab;
            EntryTitle.Focus();
            return;
        }
        if (EnableExpiry.IsChecked == true && !Expiry.SelectedDate.HasValue)
        {
            ShowValidation("EntryExpiryInvalid");
            Sections.SelectedItem = CredentialsTab;
            return;
        }
        if (!CommitField())
        {
            Sections.SelectedItem = CustomFieldsTab;
            return;
        }
        _draft.Title = EntryTitle.Text;
        _draft.UserName = UserName.Text ?? "";
        _draft.Password = Password.Text ?? "";
        _draft.Url = Url.Text ?? "";
        _draft.Tags = Tags.Text ?? "";
        _draft.Notes = Notes.Text ?? "";
        _draft.ExpiryDate = EnableExpiry.IsChecked == true ? Expiry.SelectedDate?.Date : null;
        _draft.AutoTypeEnabled = AutoTypeEnabled.IsChecked == true;
        _draft.AutoTypeSequence = Sequence.Text ?? "";
        if (_draft.AutoTypeEnabled)
        {
            try { AutoTypeService.Parse(_draft); }
            catch (Exception error) when (error is InvalidDataException or InvalidOperationException or OverflowException or System.Text.RegularExpressions.RegexMatchTimeoutException)
            {
                ShowValidation("EntryAutoTypeInvalid");
                Sections.SelectedItem = AutoTypeTab;
                return;
            }
        }
        _permitClose = true;
        Close(true);
    }

    private void ShowValidation(string key)
    {
        ValidationMessage.Text = _text(key, []);
        ValidationMessage.IsVisible = true;
    }

    internal Task WaitForOperationsAsync() => _operation;

    private async void LoadSelectedDetails(object? sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, Sections)) return;
        if (ReferenceEquals(Sections.SelectedItem, AttachmentsTab) && !_editDraft.AttachmentsLoaded)
            await RunEditorOperationAsync(_editDraft.LoadAttachmentsAsync, "LoadFailed");
        else if (ReferenceEquals(Sections.SelectedItem, HistoryTab) && !_editDraft.HistoryLoaded)
            await RunEditorOperationAsync(LoadHistoryAsync, "LoadFailed");
    }

    private async Task LoadHistoryAsync(CancellationToken token)
    {
        await _editDraft.LoadHistoryAsync(token);
        token.ThrowIfCancellationRequested();
        RefreshHistory();
    }

    private Task RunEditorOperationAsync(Func<CancellationToken, Task> action, string failureKey)
    {
        if (_closed || _busy) return Task.CompletedTask;
        _operation = RunAsync(_lifetime.Token);
        return _operation;

        async Task RunAsync(CancellationToken token)
        {
            _busy = true;
            Sections.IsEnabled = Save.IsEnabled = false;
            ValidationMessage.IsVisible = false;
            try { await action(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex)
            {
                if (!_closed)
                {
                    AppLogger.LogError("Entry editor operation failed", ex);
                    ShowValidation(ex is VaultConflictException ? "ErrorConflict" : failureKey);
                }
            }
            finally
            {
                _busy = false;
                if (!_closed) Sections.IsEnabled = Save.IsEnabled = true;
            }
        }
    }
}
