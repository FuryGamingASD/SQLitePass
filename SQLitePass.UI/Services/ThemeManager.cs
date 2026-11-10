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
                res["SidebarBg"] = new SolidColorBrush(Color.FromRgb(11, 15, 25));        // #0B0F19
                res["InputBg"] = new SolidColorBrush(Color.FromRgb(15, 23, 42));          // #0F172A
                res["HoverBg"] = new SolidColorBrush(Color.FromRgb(51, 65, 85));          // #334155
                res["BorderColor"] = new SolidColorBrush(Color.FromRgb(51, 65, 85));      // #334155
                res["BorderSubtle"] = new SolidColorBrush(Color.FromRgb(30, 41, 59));     // #1E293B
                res["TextPrimary"] = new SolidColorBrush(Color.FromRgb(248, 250, 252));   // #F8FAFC
                res["TextSecondary"] = new SolidColorBrush(Color.FromRgb(148, 163, 184)); // #94A3B8
                res["TextMuted"] = new SolidColorBrush(Color.FromRgb(100, 116, 139));     // #64748B
                res["AccentColor"] = new SolidColorBrush(Color.FromRgb(56, 189, 248));    // #38BDF8 Cyan
                res["AccentBg"] = new SolidColorBrush(Color.FromRgb(14, 58, 95));         // #0E3A5F
                res["IconBadgeBg"] = new SolidColorBrush(Color.FromRgb(40, 53, 72));      // #283548
                res["RowAltBg"] = new SolidColorBrush(Color.FromRgb(22, 32, 50));         // #162032 Alternating Row
                res["SelectionBg"] = new SolidColorBrush(Color.FromRgb(37, 99, 235));     // #2563EB
                res["SelectionFg"] = new SolidColorBrush(Colors.White);
                res["StatusBarBg"] = new SolidColorBrush(Color.FromRgb(9, 13, 22));       // #090D16
                res["MenuBg"] = new SolidColorBrush(Color.FromRgb(15, 23, 42));           // #0F172A
                res["ToolbarBg"] = new SolidColorBrush(Color.FromRgb(24, 34, 53));        // #182235
                res["HeaderBg"] = new SolidColorBrush(Color.FromRgb(11, 15, 25));         // #0B0F19
                res["BadgeBg"] = new SolidColorBrush(Color.FromRgb(30, 58, 95));          // #1E3A5F
                res["BadgeSuccessBg"] = new SolidColorBrush(Color.FromRgb(6, 78, 59));    // #064E3B
                res["BadgeSuccessFg"] = new SolidColorBrush(Color.FromRgb(52, 211, 153)); // #34D399
                res["BadgeSuccessBorder"] = new SolidColorBrush(Color.FromRgb(5, 150, 105)); // #059669
            }
            else
            {
                // Vibrant & Elegant Light Palette (Not plain flat white!)
                // Uses cool soft slate canvas, crisp white cards, ice-blue sidebar, and alternating soft blue-tinted rows
                res["AppBg"] = new SolidColorBrush(Color.FromRgb(241, 245, 249));         // #F1F5F9 Cool Slate Canvas
                res["CardBg"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));        // #FFFFFF Pure White Card
                res["SidebarBg"] = new SolidColorBrush(Color.FromRgb(235, 243, 252));     // #EBF3FC Distinct Tinted Sidebar!
                res["InputBg"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));       // #FFFFFF Clean Input
                res["HoverBg"] = new SolidColorBrush(Color.FromRgb(224, 236, 251));       // #E0ECFB Soft Blue Hover
                res["BorderColor"] = new SolidColorBrush(Color.FromRgb(203, 213, 225));   // #CBD5E1 Defined Slate Border
                res["BorderSubtle"] = new SolidColorBrush(Color.FromRgb(226, 232, 240));  // #E2E8F0 Subtle Border
                res["TextPrimary"] = new SolidColorBrush(Color.FromRgb(15, 23, 42));      // #0F172A Deep Navy Text
                res["TextSecondary"] = new SolidColorBrush(Color.FromRgb(71, 85, 105));   // #475569 Slate Muted Text
                res["TextMuted"] = new SolidColorBrush(Color.FromRgb(100, 116, 139));     // #64748B
                res["AccentColor"] = new SolidColorBrush(Color.FromRgb(37, 99, 235));     // #2563EB Vibrant Royal Blue
                res["AccentBg"] = new SolidColorBrush(Color.FromRgb(219, 234, 254));      // #DBEAFE Light Blue Pill
                res["IconBadgeBg"] = new SolidColorBrush(Color.FromRgb(232, 240, 254));   // #E8F0FE Soft Blue Icon Badge
                res["RowAltBg"] = new SolidColorBrush(Color.FromRgb(238, 245, 254));      // #EEF5FE Clear Alternating Sky-Tinted Row!
                res["SelectionBg"] = new SolidColorBrush(Color.FromRgb(37, 99, 235));     // #2563EB Vibrant Blue Selection
                res["SelectionFg"] = new SolidColorBrush(Colors.White);
                res["StatusBarBg"] = new SolidColorBrush(Color.FromRgb(226, 232, 240));   // #E2E8F0 Defined Status Footer
                res["MenuBg"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));        // #FFFFFF Clean Menu
                res["ToolbarBg"] = new SolidColorBrush(Color.FromRgb(248, 250, 252));     // #F8FAFC Soft Toolbar
                res["HeaderBg"] = new SolidColorBrush(Color.FromRgb(230, 238, 248));      // #E6EEF8 Distinct Column Headers
                res["BadgeBg"] = new SolidColorBrush(Color.FromRgb(224, 236, 251));       // #E0ECFB Soft Badge
                res["BadgeSuccessBg"] = new SolidColorBrush(Color.FromRgb(220, 252, 231));// #DCFCE7 Mint Green
                res["BadgeSuccessFg"] = new SolidColorBrush(Color.FromRgb(21, 128, 61));  // #15803D Forest Green
                res["BadgeSuccessBorder"] = new SolidColorBrush(Color.FromRgb(134, 239, 172)); // #86EFAC
            }
        }
    }
}
