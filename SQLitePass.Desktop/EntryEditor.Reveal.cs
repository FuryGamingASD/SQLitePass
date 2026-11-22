using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace SQLitePass.Desktop;

public sealed partial class EntryEditor
{
    private readonly DispatcherTimer _revealTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private TextBox? _revealedInput;

    private void InitializeSensitiveReveals()
    {
        RevealPassword.Content = _text("ShowPassword", []);
        RevealField.Content = _text("FieldReveal", []);
        _revealTimer.Tick += (_, _) => HideSensitiveValue();
        Deactivated += (_, _) => HideSensitiveValue();
        Sections.SelectionChanged += (_, _) => HideSensitiveValue();
        foreach (var input in new[] { Password, FieldValue })
        {
            input.AddHandler(TextBox.CopyingToClipboardEvent, SuppressClipboard);
            input.AddHandler(TextBox.CuttingToClipboardEvent, SuppressClipboard);
        }
    }

    private static void SuppressClipboard(object? sender, RoutedEventArgs e) => e.Handled = true;

    internal void RevealPassword_Click(object? sender, RoutedEventArgs e) => ToggleReveal(Password, RevealPassword);
    private void RevealField_Click(object? sender, RoutedEventArgs e) => ToggleReveal(FieldValue, RevealField);

    private void ToggleReveal(TextBox input, Button action)
    {
        bool wasVisible = ReferenceEquals(_revealedInput, input);
        HideSensitiveValue();
        if (wasVisible || _closed || string.IsNullOrEmpty(input.Text)) return;
        _revealedInput = input;
        input.RevealPassword = true;
        action.Content = _text("HidePassword", []);
        _revealTimer.Start();
    }

    private void HideSensitiveValue()
    {
        if (_revealedInput == null) return;
        _revealTimer.Stop();
        _revealedInput.RevealPassword = false;
        _revealedInput = null;
        RevealPassword.Content = _text("ShowPassword", []);
        RevealField.Content = _text("FieldReveal", []);
    }
}
