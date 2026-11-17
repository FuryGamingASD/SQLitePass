using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;

namespace SQLitePass.UI.Controls;

public enum AppIconKind { Add, Edit, Trash, User, Key, Globe, Keyboard, Generate, Theme, Settings, Lock, Eye, EyeOff, Folder, Search, Shield, Copy, ChevronDown, Mail, Card, Briefcase, Chat, Cart, Monitor, Code, Gamepad, Wifi }

public sealed class AppIcon : Control
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(AppIconKind), typeof(AppIcon),
        new FrameworkPropertyMetadata(AppIconKind.Key, FrameworkPropertyMetadataOptions.AffectsRender));
    public AppIconKind Kind { get => (AppIconKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    private static readonly IReadOnlyDictionary<AppIconKind, Geometry> Shapes = CreateShapes();

    public AppIcon()
    {
        Width = Height = 16;
        Focusable = false;
        IsHitTestVisible = false;
    }

    protected override AutomationPeer? OnCreateAutomationPeer() => null;

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var pen = new Pen(Foreground, 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        drawingContext.PushTransform(new ScaleTransform(ActualWidth / 24, ActualHeight / 24));
        drawingContext.DrawGeometry(null, pen, Shapes[Kind]); drawingContext.Pop();
    }

    private static IReadOnlyDictionary<AppIconKind, Geometry> CreateShapes()
    {
        var paths = new Dictionary<AppIconKind, string>
        {
            [AppIconKind.Add] = "M5,12 L19,12 M12,5 L12,19",
            [AppIconKind.Edit] = "M4,17 L4,20 L7,20 L20,7 L17,4 Z M14,7 L17,10",
            [AppIconKind.Trash] = "M4,7 L20,7 M9,4 L15,4 M6,7 L7,21 L17,21 L18,7 M10,10 L10,18 M14,10 L14,18",
            [AppIconKind.User] = "M8,7 A4,4 0 1 0 16,7 A4,4 0 1 0 8,7 M4,21 C4,13 20,13 20,21",
            [AppIconKind.Key] = "M17,3 C23,3 23,13 17,13 C11,13 11,3 17,3 Z M13,12 L3,22 M5,20 L7,22 M8,17 L10,19",
            [AppIconKind.Globe] = "M21,12 A9,9 0 1 0 3,12 A9,9 0 1 0 21,12 M3,12 L21,12 M12,3 C6,7 6,17 12,21 M12,3 C18,7 18,17 12,21",
            [AppIconKind.Keyboard] = "M3,5 L21,5 L21,19 L3,19 Z M6,9 L7,9 M11,9 L12,9 M16,9 L17,9 M6,13 L7,13 M11,13 L12,13 M16,13 L17,13 M8,16 L16,16",
            [AppIconKind.Generate] = "M4,18 L15,7 L18,10 L7,21 Z M16,2 L16,4 M20,5 L22,5 M20,1 L19,2 M3,6 L5,6 M4,5 L4,7",
            [AppIconKind.Theme] = "M20,14 A8,8 0 1 1 10,4 A6,6 0 0 0 20,14 Z",
            [AppIconKind.Settings] = "M12,8 A4,4 0 1 0 12,16 A4,4 0 1 0 12,8 M12,2 L12,5 M12,19 L12,22 M2,12 L5,12 M19,12 L22,12 M5,5 L7,7 M17,17 L19,19 M5,19 L7,17 M17,7 L19,5",
            [AppIconKind.Lock] = "M5,10 L19,10 L19,21 L5,21 Z M8,10 L8,7 A4,4 0 0 1 16,7 L16,10 M12,14 L12,17",
            [AppIconKind.Eye] = "M2,12 C7,3 17,3 22,12 C17,21 7,21 2,12 Z M15,12 A3,3 0 1 0 9,12 A3,3 0 1 0 15,12",
            [AppIconKind.EyeOff] = "M2,12 C7,3 17,3 22,12 C17,21 7,21 2,12 Z M15,12 A3,3 0 1 0 9,12 A3,3 0 1 0 15,12 M3,3 L21,21",
            [AppIconKind.Folder] = "M3,6 L10,6 L12,9 L21,9 L21,20 L3,20 Z",
            [AppIconKind.Search] = "M17,10 A7,7 0 1 0 3,10 A7,7 0 1 0 17,10 M15,15 L22,22",
            [AppIconKind.Shield] = "M12,3 L21,7 L20,14 C19,18 15,21 12,22 C9,21 5,18 4,14 L3,7 Z M8,12 L11,15 L17,9",
            [AppIconKind.Copy] = "M8,8 L21,8 L21,21 L8,21 Z M4,16 L3,16 L3,3 L16,3 L16,4",
            [AppIconKind.ChevronDown] = "M6,9 L12,15 L18,9",
            [AppIconKind.Mail] = "M3,5 L21,5 L21,19 L3,19 Z M3,6 L12,13 L21,6",
            [AppIconKind.Card] = "M3,5 L21,5 L21,19 L3,19 Z M3,10 L21,10 M6,15 L10,15",
            [AppIconKind.Briefcase] = "M3,7 L21,7 L21,20 L3,20 Z M8,7 L8,3 L16,3 L16,7 M3,12 L21,12 M10,12 L14,12 L14,15 L10,15 Z",
            [AppIconKind.Chat] = "M3,4 L21,4 L21,17 L10,17 L5,21 L5,17 L3,17 Z M7,9 L17,9 M7,13 L14,13",
            [AppIconKind.Cart] = "M2,3 L5,3 L8,17 L19,17 M6,7 L22,7 L20,13 L8,13 M9,20 A1,1 0 1 0 9,22 A1,1 0 1 0 9,20 M18,20 A1,1 0 1 0 18,22 A1,1 0 1 0 18,20",
            [AppIconKind.Monitor] = "M3,3 L21,3 L21,17 L3,17 Z M12,17 L12,22 M7,22 L17,22",
            [AppIconKind.Code] = "M8,6 L2,12 L8,18 M16,6 L22,12 L16,18 M14,3 L10,21",
            [AppIconKind.Gamepad] = "M7,7 L17,7 C21,7 24,20 20,21 L15,17 L9,17 L4,21 C0,20 3,7 7,7 Z M6,10 L6,14 M4,12 L8,12 M16,11 L17,11 M19,14 L20,14",
            [AppIconKind.Wifi] = "M2,8 C8,2 16,2 22,8 M5,12 C9,8 15,8 19,12 M8,16 C10,14 14,14 16,16 M12,20 L12,21"
        };
        return paths.ToDictionary(pair => pair.Key, pair => { var geometry = Geometry.Parse(pair.Value); geometry.Freeze(); return geometry; });
    }
}
