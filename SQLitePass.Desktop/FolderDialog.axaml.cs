using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SQLitePass.Desktop;

public sealed partial class FolderDialog : Window
{
    public FolderDialog() : this((key, _) => key, null) { }

    internal FolderDialog(Func<string, object[], string> text, string? currentName)
    {
        InitializeComponent();
        Title = text(currentName == null ? "GroupNewTitle" : "GroupEditTitle", []);
        Prompt.Text = text("LblGroupName", []);
        FolderName.Text = currentName ?? text("NewGroup", []);
        AcceptAction.Content = text("Save", []);
        CancelAction.Content = text("Cancel", []);
        ValidationMessage.Text = text("GroupNameRequired", []);
        Opened += (_, _) =>
        {
            if (FolderDestination.IsVisible) FolderDestination.Focus();
            else
            {
                FolderName.Focus();
                FolderName.SelectAll();
            }
        };
        Closed += (_, _) =>
        {
            FolderName.Clear();
            FolderDestination.ItemsSource = null;
        };
    }

    internal FolderDialog(Func<string, object[], string> text, string title, string label, GroupChoice[] destinations)
        : this(text, null)
    {
        Title = text(title, []);
        Prompt.Text = text(label, []);
        AcceptAction.Content = text("Ok", []);
        FolderName.IsVisible = false;
        FolderDestination.IsVisible = true;
        FolderDestination.ItemsSource = destinations;
        FolderDestination.SelectedIndex = 0;
    }

    internal string ResultName { get; private set; } = "";
    internal Guid? Destination { get; private set; }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

    internal void Accept_Click(object? sender, RoutedEventArgs e)
    {
        if (FolderDestination.IsVisible)
        {
            Destination = (FolderDestination.SelectedItem as GroupChoice)?.Id;
            if (!Destination.HasValue) return;
        }
        else
        {
            ResultName = FolderName.Text?.Trim() ?? "";
            if (ResultName.Length == 0)
            {
                ValidationMessage.IsVisible = true;
                FolderName.Focus();
                return;
            }
        }
        Close(true);
    }
}
