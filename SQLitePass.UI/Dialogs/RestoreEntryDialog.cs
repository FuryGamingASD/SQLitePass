using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using SQLitePass.Core.Services;
using SQLitePass.UI.Services;

namespace SQLitePass.UI.Dialogs;

internal sealed class RestoreEntryDialog : Window
{
    private readonly ComboBox _destinations;
    public Guid? DestinationId => (_destinations.SelectedItem as Destination)?.Id;
    public sealed record Destination(Guid Id, string Path);

    public RestoreEntryDialog(IReadOnlyDictionary<Guid, string> destinations, Guid? defaultDestination)
    {
        SetResourceReference(TitleProperty, "RestoreEntry"); Width = 520; Height = 260; MinWidth = 320; MinHeight = 220;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "AppBg"); SetResourceReference(ForegroundProperty, "TextPrimary");
        var panel = new StackPanel { Margin = new Thickness(24) };
        var description = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
        description.SetResourceReference(TextBlock.TextProperty, "RestoreDestination"); panel.Children.Add(description);
        _destinations = new ComboBox { MinHeight = 36, DisplayMemberPath = nameof(Destination.Path), MaxDropDownHeight = 260 };
        _destinations.SetResourceReference(AutomationProperties.NameProperty, "RestoreDestination");
        var choices = destinations.OrderBy(pair => pair.Value, StringComparer.CurrentCultureIgnoreCase).Select(pair => new Destination(pair.Key, pair.Value)).ToArray();
        _destinations.ItemsSource = choices;
        _destinations.SelectedItem = choices.FirstOrDefault(choice => choice.Id == defaultDestination) ?? choices.FirstOrDefault();
        panel.Children.Add(_destinations);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var cancel = new Button { IsCancel = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 8) };
        cancel.SetResourceReference(ContentControl.ContentProperty, "Cancel"); cancel.Click += (_, _) => DialogResult = false;
        var restore = new Button { IsDefault = true, MinWidth = 100, IsEnabled = choices.Length > 0, Margin = new Thickness(0, 0, 0, 8) };
        restore.SetResourceReference(ContentControl.ContentProperty, "RestoreEntry");
        restore.Click += (_, _) => { if (DestinationId.HasValue) DialogResult = true; };
        actions.Children.Add(cancel); actions.Children.Add(restore); panel.Children.Add(actions);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel };
        WindowSecurityService.ApplyProtection(this, AppSettingsManager.Load().ProtectScreenCapture);
    }
}
