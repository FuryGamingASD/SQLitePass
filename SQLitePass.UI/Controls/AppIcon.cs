using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;

namespace SQLitePass.UI.Controls;

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
        return AppIconPaths.Values.ToDictionary(pair => pair.Key, pair => { var geometry = Geometry.Parse(pair.Value); geometry.Freeze(); return geometry; });
    }
}
