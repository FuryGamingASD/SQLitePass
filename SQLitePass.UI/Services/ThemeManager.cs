using System;
using System.Windows;
using System.Windows.Media;

namespace SQLitePass.UI.Services
{
    public static class ThemeManager
    {
        public static void ApplyTheme(bool isDark = true)
        {
            var res = Application.Current.Resources;

            if (isDark)
            {
                // Dark Obsidian / Slate Palette
                res["AppBg"] = new SolidColorBrush(Color.FromRgb(15, 23, 42));            // #0F172A
                res["CardBg"] = new SolidColorBrush(Color.FromRgb(30, 41, 59));           // #1E293B
                res["SidebarBg"] = new SolidColorBrush(Color.FromRgb(15, 23, 42));        // #0F172A
                res["InputBg"] = new SolidColorBrush(Color.FromRgb(15, 23, 42));          // #0F172A
                res["HoverBg"] = new SolidColorBrush(Color.FromRgb(51, 65, 85));          // #334155
                res["BorderColor"] = new SolidColorBrush(Color.FromRgb(51, 65, 85));      // #334155
                res["TextPrimary"] = new SolidColorBrush(Color.FromRgb(248, 250, 252));   // #F8FAFC
                res["TextSecondary"] = new SolidColorBrush(Color.FromRgb(148, 163, 184)); // #94A3B8
                res["TextMuted"] = new SolidColorBrush(Color.FromRgb(100, 116, 139));     // #64748B
                res["AccentColor"] = new SolidColorBrush(Color.FromRgb(56, 189, 248));    // #38BDF8 Cyan
                res["RowAltBg"] = new SolidColorBrush(Color.FromRgb(24, 34, 53));         // #182235
                res["SelectionBg"] = new SolidColorBrush(Color.FromRgb(37, 99, 235));     // #2563EB
                res["StatusBarBg"] = new SolidColorBrush(Color.FromRgb(11, 15, 25));      // #0B0F19
                res["MenuBg"] = new SolidColorBrush(Color.FromRgb(15, 23, 42));           // #0F172A
            }
            else
            {
                // Clean Light Palette
                res["AppBg"] = new SolidColorBrush(Color.FromRgb(248, 250, 252));
                res["CardBg"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));
                res["SidebarBg"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));
                res["InputBg"] = new SolidColorBrush(Color.FromRgb(248, 250, 252));
                res["HoverBg"] = new SolidColorBrush(Color.FromRgb(241, 245, 249));
                res["BorderColor"] = new SolidColorBrush(Color.FromRgb(226, 232, 240));
                res["TextPrimary"] = new SolidColorBrush(Color.FromRgb(15, 23, 42));
                res["TextSecondary"] = new SolidColorBrush(Color.FromRgb(71, 85, 105));
                res["TextMuted"] = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                res["AccentColor"] = new SolidColorBrush(Color.FromRgb(37, 99, 235));
                res["RowAltBg"] = new SolidColorBrush(Color.FromRgb(248, 250, 252));
                res["SelectionBg"] = new SolidColorBrush(Color.FromRgb(37, 99, 235));
                res["StatusBarBg"] = new SolidColorBrush(Color.FromRgb(248, 250, 252));
                res["MenuBg"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            }
        }
    }
}
