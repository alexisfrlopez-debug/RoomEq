using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace RoomEq.App.Controls;

/// <summary>
/// VU-mètre stéréo vertical à crêtes (style console) : deux barres avec zones
/// vert / jaune / rouge et marqueurs de crête tenus. Niveaux 0..1 mis à jour
/// par le ViewModel via un minuteur d'interface.
/// </summary>
public class VuMeter : Control
{
    public static readonly StyledProperty<double> LevelLProperty =
        AvaloniaProperty.Register<VuMeter, double>(nameof(LevelL));

    public static readonly StyledProperty<double> LevelRProperty =
        AvaloniaProperty.Register<VuMeter, double>(nameof(LevelR));

    public static readonly StyledProperty<double> PeakLProperty =
        AvaloniaProperty.Register<VuMeter, double>(nameof(PeakL));

    public static readonly StyledProperty<double> PeakRProperty =
        AvaloniaProperty.Register<VuMeter, double>(nameof(PeakR));

    public double LevelL { get => GetValue(LevelLProperty); set => SetValue(LevelLProperty, value); }
    public double LevelR { get => GetValue(LevelRProperty); set => SetValue(LevelRProperty, value); }
    public double PeakL { get => GetValue(PeakLProperty); set => SetValue(PeakLProperty, value); }
    public double PeakR { get => GetValue(PeakRProperty); set => SetValue(PeakRProperty, value); }

    static VuMeter()
    {
        AffectsRender<VuMeter>(LevelLProperty, LevelRProperty, PeakLProperty, PeakRProperty);
    }

    private static readonly IBrush BgBrush = new SolidColorBrush(Color.Parse("#12141A"));
    private static readonly IBrush FrameBrush = new SolidColorBrush(Color.Parse("#2A2F3A"));
    private static readonly IBrush GreenBrush = new SolidColorBrush(Color.Parse("#3FC26B"));
    private static readonly IBrush YellowBrush = new SolidColorBrush(Color.Parse("#FFC53D"));
    private static readonly IBrush RedBrush = new SolidColorBrush(Color.Parse("#F0533F"));
    private static readonly IBrush PeakBrush = new SolidColorBrush(Color.Parse("#E8EAF0"));
    private static readonly IBrush DbBrush = new SolidColorBrush(Color.Parse("#5A6178"));

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 4 || h <= 4) return;

        ctx.FillRectangle(BgBrush, new Rect(0, 0, w, h));
        ctx.DrawRectangle(null, new Pen(FrameBrush, 1), new Rect(0.5, 0.5, w - 1, h - 1));

        // graduations -12 / -6 / 0 dB (0 dB ≈ 0,89 plein échelle conservateur)
        double db0 = H(0.89, h), db6 = H(0.45, h), db12 = H(0.25, h);
        var pen = new Pen(DbBrush, 1, dashStyle: DashStyle.Dash);
        ctx.DrawLine(pen, new Point(2, db0), new Point(w - 2, db0));
        ctx.DrawLine(pen, new Point(2, db6), new Point(w - 2, db6));
        ctx.DrawLine(pen, new Point(2, db12), new Point(w - 2, db12));

        double gap = 3;
        double barW = Math.Max(2, (w - gap * 3) / 2);
        DrawBar(ctx, 2, barW, h, LevelL, PeakL);
        DrawBar(ctx, 2 + barW + gap, barW, h, LevelR, PeakR);
    }

    private static double H(double t, double h) => h * (1 - Math.Clamp(t, 0, 1)) + 1;

    private static void DrawBar(DrawingContext ctx, double x, double w, double h, double level, double peak)
    {
        level = Math.Clamp(level, 0, 1);
        double full = h - 2;

        // segments colorés : vert 0-60 %, jaune 60-85 %, rouge 85-100 %
        double lv = level * full;
        double gEnd = Math.Min(lv, 0.60 * full);
        double yEnd = Math.Min(lv, 0.85 * full);
        double rEnd = lv;

        if (gEnd > 0)
            ctx.FillRectangle(GreenBrush, new Rect(x, h - 1 - gEnd, w, gEnd));
        if (yEnd > gEnd)
            ctx.FillRectangle(YellowBrush, new Rect(x, h - 1 - yEnd, w, yEnd - gEnd));
        if (rEnd > yEnd)
            ctx.FillRectangle(RedBrush, new Rect(x, h - 1 - rEnd, w, rEnd - yEnd));

        // marqueur de crête
        if (peak > 0.005)
        {
            double py = h - 1 - Math.Clamp(peak, 0, 1) * full;
            ctx.FillRectangle(PeakBrush, new Rect(x, py - 1, w, 2));
        }
    }
}
