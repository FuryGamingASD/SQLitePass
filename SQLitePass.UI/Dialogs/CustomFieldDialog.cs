using System.Windows;
using System.Windows.Controls;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;
using SQLitePass.UI.Services;

namespace SQLitePass.UI.Dialogs;

internal sealed class CustomFieldDialog : Window
{
    public PwCustomField? Result { get; private set; }
    public CustomFieldDialog(PwCustomField? field = null)
    {
        SetResourceReference(TitleProperty, "CustomFieldTitle"); Width = 460; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; SetResourceReference(BackgroundProperty, "AppBg");
        var panel = new StackPanel { Margin = new Thickness(24) };
        void Label(string resourceKey)
        {
            var label = new TextBlock { Margin = new Thickness(0, 8, 0, 6) };
            label.SetResourceReference(TextBlock.TextProperty, resourceKey);
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary"); panel.Children.Add(label);
        }
        Label("FieldName"); var name = new TextBox { Text = field?.Key ?? "" }; panel.Children.Add(name);
        name.SetResourceReference(System.Windows.Automation.AutomationProperties.NameProperty, "FieldName");
        Label("FieldValue"); var secret = new PasswordBox { Password = field?.Value ?? "" }; panel.Children.Add(secret);
        secret.SetResourceReference(System.Windows.Automation.AutomationProperties.NameProperty, "FieldValue");
        var plain = new TextBox { Visibility = Visibility.Collapsed, IsUndoEnabled = false }; panel.Children.Add(plain);
        plain.SetResourceReference(System.Windows.Automation.AutomationProperties.NameProperty, "FieldValue");
        var reveal = new CheckBox { Margin = new Thickness(0, 12, 0, 8) }; panel.Children.Add(reveal);
        reveal.SetResourceReference(ContentControl.ContentProperty, "FieldReveal");
        bool revealed = false;
        var timedReveal = new TimedSecretReveal(show =>
        {
            string value = revealed ? plain.Text : secret.Password;
            revealed = show;
            if (show) { plain.Text = value; secret.Clear(); }
            else { secret.Password = value; plain.Clear(); }
            plain.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            secret.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
            reveal.IsChecked = show;
        });
        reveal.Checked += (_, _) => { if (!timedReveal.IsRevealed) timedReveal.Reveal(); };
        reveal.Unchecked += (_, _) => timedReveal.Hide();
        Deactivated += (_, _) => timedReveal.Hide();
        var protect = new CheckBox { IsChecked = field?.IsProtected ?? true }; panel.Children.Add(protect);
        protect.SetResourceReference(ContentControl.ContentProperty, "FieldMask");
        var save = new Button { IsDefault = true, Margin = new Thickness(0, 18, 0, 0) }; panel.Children.Add(save);
        save.SetResourceReference(ContentControl.ContentProperty, "SaveField");
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) { name.Focus(); return; }
            Result = new PwCustomField { Id = field?.Id ?? Guid.NewGuid(), EntryId = field?.EntryId ?? Guid.Empty, Key = name.Text.Trim(), Value = reveal.IsChecked == true ? plain.Text : secret.Password, IsProtected = protect.IsChecked == true };
            DialogResult = true;
        };
        Content = panel; Closed += (_, _) => { timedReveal.Dispose(); secret.Clear(); plain.Clear(); };
        WindowSecurityService.ApplyProtection(this, AppSettingsManager.Load().ProtectScreenCapture);
    }
}
