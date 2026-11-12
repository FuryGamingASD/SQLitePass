using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace SQLitePass.UI.Services
{
    public enum DropPosition
    {
        None,
        Before,
        Inside,
        After
    }

    /// <summary>
    /// Visual adorner that renders a crisp insertion line (for reordering before/after)
    /// or a soft highlight box (when nesting inside a parent group) during Drag & Drop.
    /// Attached to the TreeView container for smooth, flicker-free rendering.
    /// </summary>
    public class TreeViewDropAdorner : Adorner
    {
        private DropPosition _position = DropPosition.None;
        private Rect _targetRect = Rect.Empty;

        private static readonly Pen LinePen;
        private static readonly Brush HighlightFill;
        private static readonly Pen HighlightBorder;
        private static readonly Brush CircleBrush;

        static TreeViewDropAdorner()
        {
            var accentColor = Color.FromRgb(37, 99, 235); // #2563EB Vibrant Blue

            LinePen = new Pen(new SolidColorBrush(accentColor), 2.0);
            LinePen.Freeze();

            HighlightFill = new SolidColorBrush(Color.FromArgb(45, accentColor.R, accentColor.G, accentColor.B));
            HighlightFill.Freeze();

            HighlightBorder = new Pen(new SolidColorBrush(Color.FromArgb(180, accentColor.R, accentColor.G, accentColor.B)), 1.5);
            HighlightBorder.Freeze();

            CircleBrush = new SolidColorBrush(accentColor);
            CircleBrush.Freeze();
        }

        public TreeViewDropAdorner(UIElement adornedElement) : base(adornedElement)
        {
            IsHitTestVisible = false;
        }

        public void UpdateTarget(Rect targetRect, DropPosition position)
        {
            if (_position != position || _targetRect != targetRect)
            {
                _position = position;
                _targetRect = targetRect;
                InvalidateVisual();
            }
        }

        public void Clear()
        {
            if (_position != DropPosition.None)
            {
                _position = DropPosition.None;
                _targetRect = Rect.Empty;
                InvalidateVisual();
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            if (_position == DropPosition.None || _targetRect.IsEmpty || _targetRect.Width <= 0 || _targetRect.Height <= 0)
                return;

            double x = _targetRect.X;
            double y = _targetRect.Y;
            double w = _targetRect.Width;
            double h = _targetRect.Height;

            if (_position == DropPosition.Before)
            {
                // Top insertion line with anchor dot on the left
                dc.DrawEllipse(CircleBrush, null, new Point(x + 4, y), 3.5, 3.5);
                dc.DrawLine(LinePen, new Point(x + 6, y), new Point(x + w, y));
            }
            else if (_position == DropPosition.After)
            {
                // Bottom insertion line with anchor dot on the left
                dc.DrawEllipse(CircleBrush, null, new Point(x + 4, y + h), 3.5, 3.5);
                dc.DrawLine(LinePen, new Point(x + 6, y + h), new Point(x + w, y + h));
            }
            else if (_position == DropPosition.Inside)
            {
                // Soft highlight box embracing the target parent group
                dc.DrawRoundedRectangle(HighlightFill, HighlightBorder, new Rect(x, y, w, h), 5, 5);
            }
        }
    }
}
