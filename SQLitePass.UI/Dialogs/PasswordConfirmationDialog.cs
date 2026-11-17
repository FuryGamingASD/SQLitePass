using System.Windows;
using System.Windows.Controls;
using SQLitePass.Core.Services;
using SQLitePass.UI.Services;

namespace SQLitePass.UI.Dialogs;

internal sealed class PasswordConfirmationDialog : Window
{
    public PasswordConfirmationDialog(string expected)
    {
        SetResourceReference(TitleProperty, "ConfirmNewVaultTitle"); Width = 420; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize;
        SetResourceReference(BackgroundProperty, "AppBg");
        var panel = new StackPanel { Margin = new Thickness(24) };
        var label = new TextBlock { Margin = new Thickness(0, 0, 0, 12) };
        label.SetResourceReference(TextBlock.TextProperty, "RepeatNewMasterPassword");
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary"); panel.Children.Add(label);
        var password = new PasswordBox();
        password.SetResourceReference(System.Windows.Automation.AutomationProperties.NameProperty, "ConfirmMasterPassword"); panel.Children.Add(password);
        var save = new Button { IsDefault = true, Margin = new Thickness(0, 16, 0, 0) };
        save.SetResourceReference(ContentControl.ContentProperty, "CreateVault");
        save.Click += (_, _) =>
        {
            if (password.Password != expected) { MessageBox.Show(this, LocalizationManager.GetString("PasswordMismatch"), LocalizationManager.GetString("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning); password.Clear(); password.Focus(); return; }
            DialogResult = true;
        };
        panel.Children.Add(save); Content = panel; Loaded += (_, _) => password.Focus();
        Closed += (_, _) => { password.Clear(); expected = string.Empty; };
        WindowSecurityService.ApplyProtection(this, AppSettingsManager.Load().ProtectScreenCapture);
    }
}
