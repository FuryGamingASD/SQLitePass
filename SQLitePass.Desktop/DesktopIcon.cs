using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using SQLitePass.UI.Controls;

namespace SQLitePass.Desktop;

public sealed class DesktopIcon : TemplatedControl
{
    public static readonly StyledProperty<AppIconKind> KindProperty =
        AvaloniaProperty.Register<DesktopIcon, AppIconKind>(nameof(Kind));

    private static readonly ConditionalWeakTable<Dispatcher, IReadOnlyDictionary<AppIconKind, Geometry>> Shapes = new();

    static DesktopIcon() => AffectsRender<DesktopIcon>(KindProperty, ForegroundProperty);

    public DesktopIcon()
    {
        Width = Height = 18;
        IsHitTestVisible = false;
        Focusable = false;
    }

    public AppIconKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var pen = new Pen(Foreground, 1.8, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        using var scale = context.PushTransform(Matrix.CreateScale(Bounds.Width / 24, Bounds.Height / 24));
        var shapes = Shapes.GetValue(Dispatcher, static _ =>
            AppIconPaths.Values.ToDictionary(path => path.Key, path => Geometry.Parse(path.Value)));
        context.DrawGeometry(null, pen, shapes[Kind]);
    }
}
