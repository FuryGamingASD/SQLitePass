using System.Windows;
using System.Windows.Data;
using System.Windows.Automation;
using SQLitePass.UI.Services;

namespace SQLitePass.UI;

public partial class MainWindow
{
    private TimedSecretReveal _previewReveal = null!, _columnReveal = null!;
    private void InitializeSensitiveReveals()
    {
        _previewReveal = new TimedSecretReveal(revealed =>
        {
            _previewPasswordRevealed = revealed;
            string password = _selectedEntry?.Password ?? "";
            PrevPassword.Text = revealed && !_isLocked ? UiText.Preview(password, 512) : new string('•', Math.Min(password.Length, 14));
            BtnPreviewReveal.Content = new Controls.AppIcon { Kind = revealed ? Controls.AppIconKind.EyeOff : Controls.AppIconKind.Eye };
            BtnPreviewReveal.SetResourceReference(ToolTipProperty, revealed ? "HidePassword" : "ShowPassword");
            BtnPreviewReveal.SetResourceReference(AutomationProperties.NameProperty, revealed ? "HidePassword" : "ShowPassword");
        });
        _columnReveal = new TimedSecretReveal(revealed =>
        {
            _showPasswordColumn = revealed;
            ColPassword.Binding = revealed ?
                new Binding("Password") { Converter = (IValueConverter)Resources["TextPreviewConverter"], ConverterParameter = 512 } :
                new Binding("Password") { Converter = (IValueConverter)Resources["PasswordMaskConverter"] };
        });
        Deactivated += (_, _) => HideRevealedSecrets();
    }
    private void HideRevealedSecrets() { _previewReveal?.Hide(); _columnReveal?.Hide(); }
}
