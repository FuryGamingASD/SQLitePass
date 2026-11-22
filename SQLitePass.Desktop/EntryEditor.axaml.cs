using Avalonia.Controls;
using Avalonia.Interactivity;
using SQLitePass.Core.Models;

namespace SQLitePass.Desktop;

public sealed partial class EntryEditor : Window
{
    private readonly PwEntry _draft;

    public EntryEditor() : this(new PwEntry(), (key, _) => key) { }

    internal EntryEditor(PwEntry draft, Func<string, object[], string> text)
    {
        _draft = draft;
        InitializeComponent();
        Title = text(draft.Revision > 0 ? "EntryEditTitle" : "EntryNewTitle", []);
        TitleLabel.Text = text("LblTitle", []);
        UserLabel.Text = text("LblUserName", []);
        PasswordLabel.Text = text("LblPassword", []);
        UrlLabel.Text = text("LblUrl", []);
        TagsLabel.Text = text("LblTags", []);
        NotesLabel.Text = text("LblNotes", []);
        ValidationMessage.Text = text("TitleRequired", []);
        Cancel.Content = text("Cancel", []);
        Save.Content = text("Save", []);

        EntryTitle.Text = draft.Title;
        UserName.Text = draft.UserName;
        Password.Text = draft.Password;
        Url.Text = draft.Url;
        Tags.Text = draft.Tags;
        Notes.Text = draft.Notes;
        Opened += (_, _) => EntryTitle.Focus();
        Closed += (_, _) =>
        {
            EntryTitle.Clear(); UserName.Clear(); Password.Clear(); Url.Clear(); Tags.Clear(); Notes.Clear();
        };
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

    internal void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(EntryTitle.Text))
        {
            ValidationMessage.IsVisible = true;
            EntryTitle.Focus();
            return;
        }
        _draft.Title = EntryTitle.Text;
        _draft.UserName = UserName.Text ?? "";
        _draft.Password = Password.Text ?? "";
        _draft.Url = Url.Text ?? "";
        _draft.Tags = Tags.Text ?? "";
        _draft.Notes = Notes.Text ?? "";
        Close(true);
    }
}
