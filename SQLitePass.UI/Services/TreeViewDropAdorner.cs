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
    /// Visual adorner that renders an insertion line (for reordering before/after)
    /// or a soft highlight box (when nesting inside a parent group) during Drag & Drop.
    /// </summary>
    public class TreeViewDropAdorner : Adorner
    {
        private DropPosition _position;
        private static readonly Pen LinePen;
        private static readonly Brush HighlightFill;
        private static readonly Pen HighlightBorder;
        private static readonly Brush CircleBrush;

        static TreeViewDropAdorner()
        {
            var accentColor = Color.FromRgb(37, 99, 235); // #2563EB Vibrant Blue

            LinePen = new Pen(new SolidColorBrush(accentColor), 2.5);
            LinePen.Freeze();

            HighlightFill = new SolidColorBrush(Color.FromArgb(45, accentColor.R, accentColor.G, accentColor.B));
            HighlightFill.Freeze();

            HighlightBorder = new Pen(new SolidColorBrush(Color.FromArgb(180, accentColor.R, accentColor.G, accentColor.B)), 1.5);
            HighlightBorder.Freeze();

            CircleBrush = new SolidColorBrush(accentColor);
            CircleBrush.Freeze();
        }

        public TreeViewDropAdorner(UIElement adornedElement, DropPosition position) : base(adornedElement)
        {
            _position = position;
            IsHitTestVisible = false;
        }

        public void UpdatePosition(DropPosition position)
        {
            if (_position != position)
            {
                _position = position;
                InvalidateVisual();
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double w = AdornedElement.RenderSize.Width;
            double h = AdornedElement.RenderSize.Height;
            if (w <= 0) w = 180;
            if (h <= 0) h = 24;

            if (_position == DropPosition.Before)
            {
                // Top insertion line with dot on the left
                dc.DrawEllipse(CircleBrush, null, new Point(4, 1), 3.5, 3.5);
                dc.DrawLine(LinePen, new Point(6, 1), new Point(w, 1));
            }
            else if (_position == DropPosition.After)
            {
                // Bottom insertion line with dot on the left
                dc.DrawEllipse(CircleBrush, null, new Point(4, h - 1), 3.5, 3.5);
                dc.DrawLine(LinePen, new Point(6, h - 1), new Point(w, h - 1));
            }
            else if (_position == DropPosition.Inside)
            {
                // Soft highlight box embracing the target parent group
                dc.DrawRoundedRectangle(HighlightFill, HighlightBorder, new Rect(1, 1, Math.Max(0, w - 2), Math.Max(0, h - 2)), 5, 5);
            }
        }
    }
}
