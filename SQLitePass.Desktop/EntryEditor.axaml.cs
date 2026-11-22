using Avalonia.Controls;
using Avalonia.Interactivity;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;

namespace SQLitePass.Desktop;

public sealed partial class EntryEditor : Window
{
    private readonly PwEntry _draft;
    private readonly Func<string, object[], string> _text;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly string? _vaultPath;
    private readonly Func<PasswordGeneratorDialog>? _createGenerator;
    private Window? _confirmation;
    private Task _operation = Task.CompletedTask;
    private bool _closed;
    private bool _busy;

    public EntryEditor() : this(new PwEntry(), (key, _) => key) { }

    internal EntryEditor(PwEntry draft, Func<string, object[], string> text, string? vaultPath = null,
        Func<PasswordGeneratorDialog>? createGenerator = null)
    {
        _draft = draft;
        _text = text;
        _vaultPath = vaultPath;
        _createGenerator = createGenerator;
        InitializeComponent();
        Title = text(draft.Revision > 0 ? "EntryEditTitle" : "EntryNewTitle", []);
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

        SetCredentialFields(draft);
        InitializeCustomFields();
        InitializeAttachments();
        InitializeHistory();
        InitializeSensitiveReveals();
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

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

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
        if (_closed || _busy) return;
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
        Close(true);
    }

    private void ShowValidation(string key)
    {
        ValidationMessage.Text = _text(key, []);
        ValidationMessage.IsVisible = true;
    }

    internal Task WaitForOperationsAsync() => _operation;

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
                    ShowValidation(failureKey);
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
