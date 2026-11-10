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
                // Dark Zinc / Obsidian Palette (Bitwarden & Modern KeePassXC inspired)
                res["AppBg"] = new SolidColorBrush(Color.FromRgb(9, 9, 11));             // #09090B Zinc 950 Canvas
                res["CardBg"] = new SolidColorBrush(Color.FromRgb(18, 18, 21));          // #121215 Elevated Surface
                res["SidebarBg"] = new SolidColorBrush(Color.FromRgb(13, 13, 16));       // #0D0D10 Quiet Sidebar
                res["InputBg"] = new SolidColorBrush(Color.FromRgb(9, 9, 11));           // #09090B Input Box
                res["HoverBg"] = new SolidColorBrush(Color.FromRgb(28, 28, 33));         // #1C1C21 Zinc 850 Hover
                res["ActivePillBg"] = new SolidColorBrush(Color.FromRgb(39, 39, 47));    // #27272F Active Pill
                res["BorderColor"] = new SolidColorBrush(Color.FromRgb(39, 39, 42));     // #27272A Zinc 800 Border
                res["BorderSubtle"] = new SolidColorBrush(Color.FromRgb(30, 30, 34));    // #1E1E22 Subtle Separator
                res["TextPrimary"] = new SolidColorBrush(Color.FromRgb(244, 244, 245));  // #F4F4F5 Zinc 100
                res["TextSecondary"] = new SolidColorBrush(Color.FromRgb(161, 161, 170));// #A1A1AA Zinc 400
                res["TextMuted"] = new SolidColorBrush(Color.FromRgb(113, 113, 122));    // #71717A Zinc 500
                res["AccentColor"] = new SolidColorBrush(Color.FromRgb(37, 99, 235));    // #2563EB Brand Blue
                res["AccentHover"] = new SolidColorBrush(Color.FromRgb(59, 130, 246));   // #3B82F6 Lighter Blue
                res["AccentBg"] = new SolidColorBrush(Color.FromRgb(23, 37, 84));        // #172554 Deep Blue Tint
                res["IconBadgeBg"] = new SolidColorBrush(Color.FromRgb(24, 24, 28));     // #18181C
                res["RowAltBg"] = new SolidColorBrush(Color.FromRgb(13, 13, 17));        // #0D0D11 Subtle Alternation
                res["SelectionBg"] = new SolidColorBrush(Color.FromRgb(37, 99, 235));    // #2563EB
                res["SelectionFg"] = new SolidColorBrush(Colors.White);
                res["StatusBarBg"] = new SolidColorBrush(Color.FromRgb(11, 11, 14));     // #0B0B0E
                res["MenuBg"] = new SolidColorBrush(Color.FromRgb(18, 18, 21));          // #121215
                res["ToolbarBg"] = new SolidColorBrush(Color.FromRgb(18, 18, 21));       // #121215
                res["HeaderBg"] = new SolidColorBrush(Color.FromRgb(13, 13, 16));        // #0D0D10
                res["BadgeBg"] = new SolidColorBrush(Color.FromRgb(30, 41, 59));         // #1E293B
                res["BadgeSuccessBg"] = new SolidColorBrush(Color.FromRgb(6, 78, 59));   // #064E3B
                res["BadgeSuccessFg"] = new SolidColorBrush(Color.FromRgb(52, 211, 153));// #34D399
                res["BadgeSuccessBorder"] = new SolidColorBrush(Color.FromRgb(5, 150, 105)); // #059669
                res["ModalBackdrop"] = new SolidColorBrush(Color.FromArgb(180, 5, 5, 8)); // Dim backdrop
            }
            else
            {
                // Refined Neutral Light Palette (Slate-50 canvas, crisp white cards, slate-200 borders)
                res["AppBg"] = new SolidColorBrush(Color.FromRgb(248, 250, 252));        // #F8FAFC Slate 50 Neutral Canvas
                res["CardBg"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));       // #FFFFFF Crisp White Surface
                res["SidebarBg"] = new SolidColorBrush(Color.FromRgb(241, 245, 249));    // #F1F5F9 Slate 100 Sidebar
                res["InputBg"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));      // #FFFFFF Clean White Input
                res["HoverBg"] = new SolidColorBrush(Color.FromRgb(226, 232, 240));      // #E2E8F0 Slate 200 Hover
                res["ActivePillBg"] = new SolidColorBrush(Color.FromRgb(219, 234, 254)); // #DBEAFE Blue 100 Active Pill
                res["BorderColor"] = new SolidColorBrush(Color.FromRgb(203, 213, 225));  // #CBD5E1 Slate 300 Defined Border
                res["BorderSubtle"] = new SolidColorBrush(Color.FromRgb(226, 232, 240)); // #E2E8F0 Slate 200 Subtle Line
                res["TextPrimary"] = new SolidColorBrush(Color.FromRgb(15, 23, 42));     // #0F172A Slate 900 Soft Dark Text
                res["TextSecondary"] = new SolidColorBrush(Color.FromRgb(100, 116, 139));// #64748B Slate 500 Secondary
                res["TextMuted"] = new SolidColorBrush(Color.FromRgb(148, 163, 184));    // #94A3B8 Slate 400 Muted
                res["AccentColor"] = new SolidColorBrush(Color.FromRgb(37, 99, 235));    // #2563EB Brand Blue
                res["AccentHover"] = new SolidColorBrush(Color.FromRgb(29, 78, 216));    // #1D4ED8 Darker Blue
                res["AccentBg"] = new SolidColorBrush(Color.FromRgb(239, 246, 255));     // #EFF6FF Blue 50 Pastel
                res["IconBadgeBg"] = new SolidColorBrush(Color.FromRgb(241, 245, 249));  // #F1F5F9 Slate 100
                res["RowAltBg"] = new SolidColorBrush(Color.FromRgb(248, 250, 252));     // #F8FAFC Slate 50 Row Alt
                res["SelectionBg"] = new SolidColorBrush(Color.FromRgb(37, 99, 235));    // #2563EB Blue Selection
                res["SelectionFg"] = new SolidColorBrush(Colors.White);
                res["StatusBarBg"] = new SolidColorBrush(Color.FromRgb(241, 245, 249));  // #F1F5F9 Slate 100 Status
                res["MenuBg"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));       // #FFFFFF White Menu
                res["ToolbarBg"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));    // #FFFFFF White Toolbar
                res["HeaderBg"] = new SolidColorBrush(Color.FromRgb(248, 250, 252));     // #F8FAFC Slate 50 Header
                res["BadgeBg"] = new SolidColorBrush(Color.FromRgb(239, 246, 255));      // #EFF6FF Light Badge
                res["BadgeSuccessBg"] = new SolidColorBrush(Color.FromRgb(220, 252, 231));// #DCFCE7 Emerald 100
                res["BadgeSuccessFg"] = new SolidColorBrush(Color.FromRgb(21, 128, 61)); // #15803D Emerald 700
                res["BadgeSuccessBorder"] = new SolidColorBrush(Color.FromRgb(134, 239, 172)); // #86EFAC
                res["ModalBackdrop"] = new SolidColorBrush(Color.FromArgb(120, 15, 23, 42)); // Dim backdrop
            }
        }
    }
}
