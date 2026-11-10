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
                // Crisp White & Vibrant Light Palette
                // Pure White is the primary base; delicate tints alternate rows and add colorful accents without darkening
                res["AppBg"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));         // #FFFFFF Pure White Canvas
                res["CardBg"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));        // #FFFFFF Pure White
                res["SidebarBg"] = new SolidColorBrush(Color.FromRgb(248, 250, 252));     // #F8FAFC Ultra-Clean Soft White/Slate
                res["InputBg"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));       // #FFFFFF Clean White Input
                res["HoverBg"] = new SolidColorBrush(Color.FromRgb(235, 243, 254));       // #EBF3FE Soft Blue-Tinted Hover
                res["BorderColor"] = new SolidColorBrush(Color.FromRgb(226, 232, 240));   // #E2E8F0 Clean Crisp Slate Border
                res["BorderSubtle"] = new SolidColorBrush(Color.FromRgb(241, 245, 249));  // #F1F5F9 Light Border
                res["TextPrimary"] = new SolidColorBrush(Color.FromRgb(15, 23, 42));      // #0F172A Deep Navy / Charcoal Text
                res["TextSecondary"] = new SolidColorBrush(Color.FromRgb(71, 85, 105));   // #475569 Secondary Slate Text
                res["TextMuted"] = new SolidColorBrush(Color.FromRgb(100, 116, 139));     // #64748B
                res["AccentColor"] = new SolidColorBrush(Color.FromRgb(37, 99, 235));     // #2563EB Vibrant Royal Blue
                res["AccentBg"] = new SolidColorBrush(Color.FromRgb(239, 246, 255));      // #EFF6FF Pastel Blue Capsule
                res["IconBadgeBg"] = new SolidColorBrush(Color.FromRgb(239, 246, 255));   // #EFF6FF Vibrant Soft Blue Badge
                res["RowAltBg"] = new SolidColorBrush(Color.FromRgb(244, 248, 253));      // #F4F8FD Delicate Light-Blue Alternating Row
                res["SelectionBg"] = new SolidColorBrush(Color.FromRgb(37, 99, 235));     // #2563EB Vibrant Blue Selection
                res["SelectionFg"] = new SolidColorBrush(Colors.White);
                res["StatusBarBg"] = new SolidColorBrush(Color.FromRgb(248, 250, 252));   // #F8FAFC Clean Light Footer
                res["MenuBg"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));        // #FFFFFF Pure White Menu
                res["ToolbarBg"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));     // #FFFFFF Pure White Toolbar
                res["HeaderBg"] = new SolidColorBrush(Color.FromRgb(241, 245, 249));      // #F1F5F9 Clean Header
                res["BadgeBg"] = new SolidColorBrush(Color.FromRgb(239, 246, 255));       // #EFF6FF Light Badge
                res["BadgeSuccessBg"] = new SolidColorBrush(Color.FromRgb(220, 252, 231));// #DCFCE7 Mint Green
                res["BadgeSuccessFg"] = new SolidColorBrush(Color.FromRgb(21, 128, 61));  // #15803D Forest Green
                res["BadgeSuccessBorder"] = new SolidColorBrush(Color.FromRgb(134, 239, 172)); // #86EFAC
            }
        }
    }
}
