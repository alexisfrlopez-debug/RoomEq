using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using RoomEq.Core.Dsp;

namespace RoomEq.App.Controls;

/// <summary>Une série de points à tracer sur un PlotControl.</summary>
public sealed class PlotSeries
{
    public string? Title { get; set; }
    public List<CurvePoint> Points { get; set; } = new();
    public Color Stroke { get; set; } = Colors.Cyan;
    public double StrokeThickness { get; set; } = 1.8;
    public bool Fill { get; set; }
    public Color FillColor { get; set; } = Color.FromArgb(40, 34, 211, 238);
    public bool Dashed { get; set; }

    public static PlotSeries Create(string title, IEnumerable<CurvePoint> points, Color stroke,
        bool fill = false, bool dashed = false, double strokeThickness = 1.8)
        => new()
        {
            Title = title,
            Points = new List<CurvePoint>(points),
            Stroke = stroke,
            Fill = fill,
            Dashed = dashed,
            StrokeThickness = strokeThickness,
            FillColor = Color.FromArgb(36, stroke.R, stroke.G, stroke.B)
        };
}

/// <summary>
/// Tracé scientifique : axe X logarithmique en fréquence (20 Hz – 20 kHz),
/// axe Y en dB. Utilisé pour l'analyseur, la calibration et les profils micro.
/// </summary>
public class PlotControl : Control
{
    private static readonly IBrush GridBrush = new SolidColorBrush(Color.Parse("#262B36"));
    private static readonly IBrush GridStrongBrush = new SolidColorBrush(Color.Parse("#343B49"));
    protected static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#8B93A7"));
    private static readonly IBrush BgBrush = new SolidColorBrush(Color.Parse("#141720"));
    private static readonly Typeface LabelTypeface = new("Segoe UI, DejaVu Sans");

    private protected const double PadLeft = 42, PadRight = 10, PadTop = 8, PadBottom = 20;

    public double XMin { get; set; } = 20;
    public double XMax { get; set; } = 20000;
    public double YMin { get; set; } = -100;
    public double YMax { get; set; } = 0;
    public double TickStepY { get; set; } = 10;
    public bool LogX { get; set; } = true;

    public List<PlotSeries> Series { get; } = new();

    public void ShowSeries(IEnumerable<PlotSeries> series)
    {
        Series.Clear();
        Series.AddRange(series);
        InvalidateVisual();
    }

    public void Clear()
    {
        Series.Clear();
        InvalidateVisual();
    }

    public void Invalidate() => InvalidateVisual();

    protected double LogXMin => Math.Log10(XMin);
    protected double LogXMax => Math.Log10(XMax);

    protected Rect PlotArea()
    {
        var bounds = Bounds;
        return new Rect(PadLeft, PadTop,
            Math.Max(10, bounds.Width - PadLeft - PadRight),
            Math.Max(10, bounds.Height - PadTop - PadBottom));
    }

    protected double XToPx(double f, Rect area)
    {
        double t = LogX
            ? (Math.Log10(Math.Max(f, 1e-6)) - LogXMin) / (LogXMax - LogXMin)
            : (f - XMin) / (XMax - XMin);
        return area.X + Math.Clamp(t, 0, 1) * area.Width;
    }

    protected double PxToX(double px, Rect area)
    {
        double t = (px - area.X) / area.Width;
        t = Math.Clamp(t, 0, 1);
        return LogX ? Math.Pow(10.0, LogXMin + t * (LogXMax - LogXMin)) : XMin + t * (XMax - XMin);
    }

    protected double YToPx(double db, Rect area)
        => area.Y + Math.Clamp((YMax - db) / (YMax - YMin), 0, 1) * area.Height;

    protected double PxToY(double py, Rect area)
    {
        double t = (py - area.Y) / area.Height;
        t = Math.Clamp(t, 0, 1);
        return YMax - t * (YMax - YMin);
    }

    protected static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);

    public override void Render(DrawingContext ctx)
    {
        var area = PlotArea();

        // fond
        ctx.FillRectangle(BgBrush, new Rect(0, 0, Bounds.Width, Bounds.Height));
        ctx.FillRectangle(new SolidColorBrush(Color.Parse("#181B23")), area);

        // grille horizontale (dB)
        for (double db = Math.Ceiling(YMin / TickStepY) * TickStepY; db <= YMax; db += TickStepY)
        {
            double y = YToPx(db, area);
            var brush = Math.Abs(db) < 1e-9 ? GridStrongBrush : GridBrush;
            ctx.DrawLine(new Pen(brush, 1, dashStyle: Math.Abs(db) < 1e-9 ? null : DashStyle.Dash),
                new Point(area.X, y), new Point(area.Right, y));
            DrawLabel(ctx, db.ToString("0", CultureInfo.InvariantCulture), area.X - 4, y, TextAlignmentEx.Right);
        }

        // grille verticale (fréquences marquantes)
        double[] freqTicks = { 20, 30, 50, 100, 200, 300, 500, 1000, 2000, 3000, 5000, 10000, 20000 };
        foreach (double f in freqTicks)
        {
            if (f < XMin * 0.999 || f > XMax * 1.001) continue;
            double x = XToPx(f, area);
            ctx.DrawLine(new Pen(GridBrush, 1, dashStyle: DashStyle.Dash),
                new Point(x, area.Y), new Point(x, area.Bottom));
            DrawLabel(ctx, FormatFreq(f), x, area.Bottom + 3, TextAlignmentEx.Center);
        }

        // cadre
        ctx.DrawRectangle(null, new Pen(GridStrongBrush, 1), area);

        // séries
        using (ctx.PushClip(area))
        {
            foreach (var s in Series)
            {
                if (s.Points.Count < 2) continue;

                var pen = new Pen(new SolidColorBrush(s.Stroke), s.StrokeThickness,
                    dashStyle: s.Dashed ? DashStyle.Dash : null, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    bool first = true;
                    foreach (var p in s.Points)
                    {
                        if (p.Freq < XMin * 0.5 || p.Freq > XMax * 2) continue;
                        var pt = new Point(XToPx(p.Freq, area), YToPx(Clamp(p.Db, YMin, YMax), area));
                        if (first)
                        {
                            g.BeginFigure(pt, isFilled: false);
                            first = false;
                        }
                        else g.LineTo(pt);
                    }
                    g.EndFigure(false);
                }
                if (s.Fill)
                {
                    // remplissage : copie fermée vers le bas de l'axe
                    var fillGeo = new StreamGeometry();
                    using (var g2 = fillGeo.Open())
                    {
                        bool first = true;
                        double lastX = 0;
                        foreach (var p in s.Points)
                        {
                            if (p.Freq < XMin * 0.5 || p.Freq > XMax * 2) continue;
                            var pt = new Point(XToPx(p.Freq, area), YToPx(Clamp(p.Db, YMin, YMax), area));
                            if (first) { g2.BeginFigure(pt, isFilled: true); first = false; }
                            else g2.LineTo(pt);
                            lastX = pt.X;
                        }
                        g2.LineTo(new Point(lastX, area.Bottom));
                        g2.LineTo(new Point(XToPx(s.Points[0].Freq, area), area.Bottom));
                        g2.EndFigure(true);
                    }
                    ctx.DrawGeometry(new SolidColorBrush(s.FillColor), null, fillGeo);
                }
                ctx.DrawGeometry(null, pen, geo);
            }
        }
    }

    protected static string FormatFreq(double f)
        => f >= 1000 ? (f / 1000).ToString("0.#", CultureInfo.InvariantCulture) + "k"
                     : f.ToString("0", CultureInfo.InvariantCulture);

    private void DrawLabel(DrawingContext ctx, string text, double x, double y, TextAlignmentEx align)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            LabelTypeface, 10, TextBrush);
        double px = align switch
        {
            TextAlignmentEx.Right => x - ft.Width,
            TextAlignmentEx.Center => x - ft.Width / 2,
            _ => x
        };
        ctx.DrawText(ft, new Point(px, y));
    }

    protected enum TextAlignmentEx { Left, Center, Right }
}
