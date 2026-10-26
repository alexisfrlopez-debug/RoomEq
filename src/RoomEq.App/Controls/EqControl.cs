using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using RoomEq.App.ViewModels;
using RoomEq.Core.Dsp;

namespace RoomEq.App.Controls;

/// <summary>
/// Vue interactive de l'égaliseur paramétrique : courbe composite, poignées de
/// bandes déplaçables à la souris (fréquence + gain), molette pour le facteur Q.
/// </summary>
public sealed class EqControl : PlotControl
{
    private const double GainRange = 15;
    private int _dragIndex = -1;

    public IReadOnlyList<BandViewModel>? Bands { get; set; }
    public int SelectedIndex { get; set; } = -1;

    public Action<int, double, double>? BandDragged { get; set; }   // (index, freq, gainDb)
    public Action<int, double>? BandQChanged { get; set; }          // (index, facteur)
    public Action<int>? BandSelected { get; set; }

    public EqControl()
    {
        XMin = 20; XMax = 20000;
        YMin = -GainRange; YMax = +GainRange;
        TickStepY = 3;
    }

    public void SetCurve(List<CurvePoint> curve)
    {
        ShowSeries(new[]
        {
            PlotSeries.Create("Réponse", curve, Color.Parse("#22D3EE"), fill: true)
        });
    }

    private Point HandlePoint(BandViewModel b, Rect area)
        => new(XToPx(b.Frequency, area), YToPx(Clamp(b.GainDb, -GainRange, GainRange), area));

    private int HitTest(Point p)
    {
        if (Bands == null) return -1;
        var area = PlotArea();
        int best = -1;
        double bestDist = 16;
        for (int i = 0; i < Bands.Count; i++)
        {
            var hp = HandlePoint(Bands[i], area);
            double d = Math.Sqrt(Math.Pow(hp.X - p.X, 2) + Math.Pow(hp.Y - p.Y, 2));
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);

        if (Bands == null) return;
        var area = PlotArea();

        for (int i = 0; i < Bands.Count; i++)
        {
            var b = Bands[i];
            if (!b.Enabled) continue;
            var center = HandlePoint(b, area);

            bool selected = i == SelectedIndex;
            var ringColor = selected ? Color.Parse("#FF9F1C") : Color.Parse("#12141A");
            var fill = b.GainDb >= 0 ? Color.Parse("#22D3EE") : Color.Parse("#F472B6");

            // poignée
            ctx.DrawEllipse(new SolidColorBrush(fill), new Pen(new SolidColorBrush(ringColor), selected ? 2.4 : 1.6),
                center, 7, 7);

            // ligne verticale jusqu'à l'axe 0 dB
            ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(90, fill.R, fill.G, fill.B)), 1),
                center, new Point(center.X, YToPx(0, area)));

            // numéro de bande
            var ft = new FormattedText((i + 1).ToString(), System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"), 9,
                selected ? new SolidColorBrush(Color.Parse("#FF9F1C")) : TextBrush);
            ctx.DrawText(ft, new Point(center.X - ft.Width / 2, center.Y - 22));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var p = e.GetPosition(this);
        int idx = HitTest(p);
        if (idx >= 0)
        {
            _dragIndex = idx;
            SelectedIndex = idx;
            BandSelected?.Invoke(idx);
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragIndex < 0 || e.Pointer.Captured != this || Bands == null) return;

        var p = e.GetPosition(this);
        var area = PlotArea();
        double f = PxToX(p.X, area);
        double g = PxToY(p.Y, area);

        f = Clamp(f, XMin, XMax);
        g = Clamp(g, -GainRange, GainRange);

        BandDragged?.Invoke(_dragIndex, f, g);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragIndex >= 0)
        {
            _dragIndex = -1;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        int idx = _dragIndex >= 0 ? _dragIndex : SelectedIndex;
        if (idx < 0) return;
        double factor = e.Delta.Y > 0 ? 1.15 : 1.0 / 1.15;
        BandQChanged?.Invoke(idx, factor);
        e.Handled = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_dragIndex < 0) Cursor = Cursor.Default;
    }
}
