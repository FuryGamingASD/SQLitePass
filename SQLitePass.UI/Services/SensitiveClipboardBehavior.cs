using System.Windows;

namespace SQLitePass.UI.Services;

public static class SensitiveClipboardBehavior
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(SensitiveClipboardBehavior), new PropertyMetadata(false, Changed));
    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);
    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not UIElement element) return;
        if (e.NewValue is true) DataObject.AddCopyingHandler(element, Copying);
        else DataObject.RemoveCopyingHandler(element, Copying);
    }
    private static void Copying(object sender, DataObjectCopyingEventArgs e)
    {
        // Text drag/drop and default Copy must not bypass the sensitive clipboard policy.
        e.CancelCommand();
        if (e.IsDragDrop) return;
        if (e.DataObject.GetData(DataFormats.UnicodeText) is string text)
            ClipboardCoordinator.Shared.CopyWithAutoClear(text, LocalizationManager.GetString("ClipboardText"));
    }
}
