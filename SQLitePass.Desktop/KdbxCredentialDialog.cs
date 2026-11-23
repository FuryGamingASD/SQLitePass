using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Media;

namespace SQLitePass.Desktop;

internal sealed record KdbxCredentialResult(string Password, string? KeyFilePath);

internal sealed class KdbxCredentialDialog : Window
{
    private readonly TextBox _password = new() { PasswordChar = '●', IsUndoEnabled = false, MinHeight = 36 };
    private readonly TextBox _keyFile = new() { MinHeight = 36 };

    public KdbxCredentialDialog(string title, string passwordLabel, string keyFileLabel,
        string browseLabel, string continueLabel, string cancelLabel, string keyFileOnlyNote)
    {
        Title = title;
        Width = 500;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        _password.PlaceholderText = passwordLabel;
        _keyFile.PlaceholderText = keyFileLabel;

        var browse = new Button { Content = browseLabel, MinHeight = 36 };
        browse.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = keyFileLabel,
                FileTypeFilter = [FilePickerFileTypes.All]
            });
            if (files.Count != 0) _keyFile.Text = files[0].TryGetLocalPath() ?? string.Empty;
        };

        var accept = new Button { Content = continueLabel, MinHeight = 38, IsDefault = true };
        accept.Click += (_, _) => Close(new KdbxCredentialResult(_password.Text ?? string.Empty,
            string.IsNullOrWhiteSpace(_keyFile.Text) ? null : _keyFile.Text.Trim()));
        var cancel = new Button { Content = cancelLabel, MinHeight = 38, IsCancel = true };
        cancel.Click += (_, _) => Close(null);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel);
        buttons.Children.Add(accept);

        var keyFileGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        keyFileGrid.Children.Add(_keyFile);
        Grid.SetColumn(browse, 1);
        keyFileGrid.Children.Add(browse);

        Content = new Border
        {
            Padding = new Thickness(20),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = passwordLabel },
                    _password,
                    new TextBlock { Text = keyFileLabel },
                    keyFileGrid,
                    new TextBlock { Text = keyFileOnlyNote,
                        Opacity = 0.8, TextWrapping = TextWrapping.Wrap },
                    buttons
                }
            }
        };

        Closed += (_, _) => _password.Clear();
    }
}
