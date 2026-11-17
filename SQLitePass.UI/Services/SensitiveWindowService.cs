using System.Windows;
using System.Windows.Controls;

namespace SQLitePass.UI.Services;

internal static class SensitiveWindowService
{
    public static void ClearControls(DependencyObject root)
    {
        if (root is PasswordBox password) password.Clear();
        if (root is TextBox text) { text.IsUndoEnabled = false; text.Clear(); }
        if (root is TextBlock block) block.Text = string.Empty;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>().ToArray()) ClearControls(child);
    }
}
