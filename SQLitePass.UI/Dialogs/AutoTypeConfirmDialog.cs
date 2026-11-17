using System.Windows;
using System.Windows.Controls;
using SQLitePass.Core.Services;
using SQLitePass.UI.Services;

namespace SQLitePass.UI.Dialogs;

internal sealed class AutoTypeConfirmDialog : Window
{
    internal AutoTypeConfirmDialog(string entryTitle, string targetTitle)
    {
        SetResourceReference(TitleProperty, "AutoTypeConfirmTitle"); Width = 520; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; SetResourceReference(BackgroundProperty, "AppBg");
        var panel = new StackPanel { Margin = new Thickness(24) };
        var text = new TextBlock
        {
            Text = LocalizationManager.GetString("AutoTypeConfirmText", entryTitle, targetTitle),
            TextWrapping = TextWrapping.Wrap, FontSize = 14
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary"); panel.Children.Add(text);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var cancel = new Button { IsCancel = true, Margin = new Thickness(0, 0, 12, 0) };
        cancel.SetResourceReference(ContentControl.ContentProperty, "Cancel");
        var accept = new Button();
        accept.SetResourceReference(ContentControl.ContentProperty, "AutoTypeConfirmAction");
        accept.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(cancel); buttons.Children.Add(accept); panel.Children.Add(buttons); Content = panel;
        Closed += (_, _) => text.Text = "";
        WindowSecurityService.ApplyProtection(this, AppSettingsManager.Load().ProtectScreenCapture);
    }
}
