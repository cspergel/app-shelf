using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace AppShelf.App;

/// <summary>Drag affordance adorners (Slice 2): a "lift" on the dragged card, a highlight + hint on
/// a valid drop target, and a thin insertion line for within-group reorder. Each attaches to the
/// adorner layer over its element and is removed via <see cref="Detach"/> when the gesture ends.</summary>
internal abstract class DragAdornerBase : Adorner
{
    protected DragAdornerBase(UIElement adorned) : base(adorned)
    {
        IsHitTestVisible = false; // never intercept the drag
    }

    public void Detach()
    {
        var layer = AdornerLayer.GetAdornerLayer(AdornedElement);
        layer?.Remove(this);
    }

    protected static AdornerLayer? LayerFor(UIElement element) => AdornerLayer.GetAdornerLayer(element);
}

/// <summary>Shadow + slight scale/opacity applied to the card being dragged.</summary>
internal sealed class LiftAdorner : DragAdornerBase
{
    private LiftAdorner(UIElement adorned) : base(adorned) { }

    public static LiftAdorner? Attach(UIElement card)
    {
        var layer = LayerFor(card);
        if (layer is null) return null;
        var a = new LiftAdorner(card);
        layer.Add(a);
        return a;
    }

    protected override void OnRender(DrawingContext dc)
    {
        // The source card is already dimmed (Opacity) and a cursor-following ghost trails the
        // pointer, so the lift only needs a quiet dashed accent outline to mark "this card left its
        // spot" — no heavy fill that would muddy the dimmed card.
        var rect = new Rect(AdornedElement.RenderSize);
        rect.Inflate(-1, -1);
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(150, 124, 106, 239)), 1.5)
        {
            DashStyle = new DashStyle(new double[] { 3, 3 }, 0),
        };
        dc.DrawRoundedRectangle(null, pen, rect, 10, 10);
    }
}

/// <summary>Outline + a "＋ add to group" / "＋ new group" hint over a valid drop target.</summary>
internal sealed class HighlightAdorner : DragAdornerBase
{
    private readonly string _hint;

    private HighlightAdorner(UIElement adorned, string hint) : base(adorned) => _hint = hint;

    public static HighlightAdorner Attach(UIElement card, string hint)
    {
        var a = new HighlightAdorner(card, hint);
        LayerFor(card)?.Add(a);
        return a;
    }

    protected override void OnRender(DrawingContext dc)
    {
        // Inset by half the pen width so the 2px accent border sits cleanly inside the card's
        // rounded corners instead of bleeding past the edge.
        var rect = new Rect(AdornedElement.RenderSize);
        rect.Inflate(-1, -1);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(30, 124, 106, 239)),
            new Pen(new SolidColorBrush(Color.FromArgb(235, 124, 106, 239)), 2), rect, 9, 9);

        if (string.IsNullOrEmpty(_hint))
            return;

        var text = new FormattedText(_hint, CultureInfo.CurrentUICulture, System.Windows.FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 12.5, Brushes.White, 1.0)
        { TextAlignment = TextAlignment.Center };

        var pad = new Size(text.Width + 18, text.Height + 8);
        var origin = new Point((rect.Width - pad.Width) / 2, 10);
        var pill = new Rect(origin, pad);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x7C, 0x6A, 0xEF)), null, pill, 6, 6);
        dc.DrawText(text, new Point(rect.Width / 2 - text.Width / 2, origin.Y + 4));
    }
}

/// <summary>A thin horizontal insertion line drawn at a given Y inside a member list.</summary>
internal sealed class InsertionLineAdorner : DragAdornerBase
{
    private double _y;

    private InsertionLineAdorner(UIElement adorned) : base(adorned) { }

    public static InsertionLineAdorner Attach(UIElement container)
    {
        var a = new InsertionLineAdorner(container);
        LayerFor(container)?.Add(a);
        return a;
    }

    public void SetY(double y)
    {
        if (Math.Abs(_y - y) < 0.5) return;
        _y = y;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = AdornedElement.RenderSize.Width;
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x7C, 0x6A, 0xEF)), 2.5);
        dc.DrawLine(pen, new Point(2, _y), new Point(w - 2, _y));
        dc.DrawEllipse(Brushes.White, pen, new Point(4, _y), 3, 3);
    }
}
