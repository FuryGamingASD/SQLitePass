using Avalonia.Controls;
using Avalonia.Interactivity;
using SQLitePass.Core.Services;

namespace SQLitePass.Desktop;

public sealed partial class PasswordGeneratorDialog : Window
{
    private readonly Func<string, object[], string> _text;
    private readonly Func<string, Task<string>>? _copy;
    private bool _initializing = true;
    private bool _copying;
    private string _accepted = "";

    public PasswordGeneratorDialog() : this((key, _) => key, null, false) { }

    internal PasswordGeneratorDialog(Func<string, object[], string> text, Func<string, Task<string>>? copy, bool allowUse)
    {
        _text = text;
        _copy = copy;
        InitializeComponent();
        Title = text("GeneratorTitle", []);
        ResultLabel.Text = text("GenResult", []);
        LengthLabel.Text = text("GenLength", []);
        Upper.Content = text("GenUpper", []);
        Lower.Content = text("GenLower", []);
        Digits.Content = text("GenDigits", []);
        Symbols.Content = text("GenSymbols", []);
        Brackets.Content = text("GenBrackets", []);
        ExcludeAmbiguous.Content = text("GenExcludeAmbiguous", []);
        Regenerate.Content = text("GenRegenerate", []);
        CopyPassword.Content = text("Copy", []);
        CopyPassword.IsVisible = copy != null;
        UsePassword.Content = text("GenUse", []);
        UsePassword.IsVisible = allowUse;
        Dismiss.Content = text(allowUse ? "Cancel" : "Close", []);
        Closed += (_, _) => Result.Text = "";
        _initializing = false;
        Generate();
    }

    internal string TakePassword()
    {
        string result = _accepted;
        _accepted = "";
        return result;
    }

    private void Options_Changed(object? sender, RoutedEventArgs e)
    {
        if (!_initializing) Generate();
    }

    private void Generate()
    {
        string password = "";
        try
        {
            password = PasswordGenerator.Generate(new PasswordGeneratorOptions
            {
                Length = (int)(Length.Value ?? 20),
                IncludeUpperCase = Upper.IsChecked == true,
                IncludeLowerCase = Lower.IsChecked == true,
                IncludeDigits = Digits.IsChecked == true,
                IncludeSpecial = Symbols.IsChecked == true,
                IncludeBrackets = Brackets.IsChecked == true,
                ExcludeLookAlike = ExcludeAmbiguous.IsChecked == true
            });
        }
        catch (ArgumentException) { }
        Result.Text = password;
        bool generated = password.Length > 0;
        double score = PasswordGenerator.CalculateEntropy(password);
        Quality.Text = generated ? _text("QualityScore", [score]) : _text("GeneratorChooseClass", []);
        QualityBar.Value = score;
        ClipboardStatus.IsVisible = false;
        CopyPassword.IsEnabled = generated && !_copying;
        UsePassword.IsEnabled = generated;
    }

    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        if (_copying || _copy == null || Result.Text is not { Length: > 0 } password) return;
        _copying = true;
        CopyPassword.IsEnabled = false;
        try
        {
            string message = await _copy(password);
            if (!IsVisible) return;
            ClipboardStatus.Text = message;
            ClipboardStatus.IsVisible = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            AppLogger.LogError("Unable to copy a generated password", error);
            ClipboardStatus.Text = _text("ClipboardBusy", []);
            ClipboardStatus.IsVisible = IsVisible;
        }
        finally
        {
            _copying = false;
            CopyPassword.IsEnabled = !string.IsNullOrEmpty(Result.Text);
        }
    }

    private void Use_Click(object? sender, RoutedEventArgs e)
    {
        if (Result.Text is not { Length: > 0 } password) return;
        _accepted = password;
        Close(true);
    }

    private void Dismiss_Click(object? sender, RoutedEventArgs e) => Close(false);
}
