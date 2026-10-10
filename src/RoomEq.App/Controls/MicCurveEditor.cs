using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using RoomEq.Core.Dsp;

namespace RoomEq.App.Controls;

/// <summary>
/// Éditeur interactif de courbe de correction micro :
///  - glisser un point (souris) pour modifier fréquence / gain ;
///  - double-clic dans le vide pour ajouter un point ;
///  - clic droit près d'un point pour le supprimer ;
///  - clic sur un point pour le sélectionner (édition numérique associée).
/// Le contrôle garde une copie locale des points (miroir du profil) : la vue
/// resynchronise via SetCurve après chaque modification du modèle.
/// </summary>
public sealed class MicCurveEditor : PlotControl
{
    private static readonly IBrush PointBrush = new SolidColorBrush(Color.Parse("#FF9F1C"));
    private static readonly IBrush PointSelBrush = new SolidColorBrush(Color.Parse("#22D3EE"));
    private static readonly IPen PointPen = new Pen(new SolidColorBrush(Color.Parse("#141720")), 1.5);

    private readonly List<CurvePoint> _points = new();
    private int _selectedIndex = -1;
    private int _dragIndex = -1;

    public const double YLimitDb = 20;

    public MicCurveEditor()
    {
        YMin = -YLimitDb;
        YMax = YLimitDb;
        TickStepY = 6;
    }

    /// <summary>Un point a été déplacé (index, fréquence, gain).</summary>
    public event Action<int, double, double>? PointDragged;
    /// <summary>Demande d'ajout d'un point à (fréquence, gain).</summary>
    public event Action<double, double>? PointAddRequested;
    /// <summary>Demande de suppression du point d'index donné.</summary>
    public event Action<int>? PointDeleteRequested;
    /// <summary>Sélection changée (index, -1 = aucune).</summary>
    public event Action<int>? SelectionChanged;

    public int SelectedIndex => _selectedIndex;

    /// <summary>Remplace le contenu affiché (appelé par la vue après chaque changement du modèle).</summary>
    public void SetCurve(IEnumerable<CurvePoint> points, int selectedIndex)
    {
        _points.Clear();
        _points.AddRange(points.OrderBy(p => p.Freq));
        _selectedIndex = (selectedIndex >= 0 && selectedIndex < _points.Count) ? selectedIndex : -1;
        _dragIndex = -1;
        ShowSeries(new[]
        {
            PlotSeries.Create("Correction micro", _points, Color.Parse("#FF9F1C"), fill: true)
        });
        InvalidateVisual();
    }

    /// <summary>Modifie localement un point pendant le glissement (sans resynchronisation complète).</summary>
    public void UpdateLocalPoint(int index, double freq, double db)
    {
        if (index < 0 || index >= _points.Count) return;
        _points[index] = new CurvePoint(freq, db);
        if (Series.Count > 0)
        {
            Series[0].Points = new List<CurvePoint>(_points);
        }
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var area = PlotArea();
        var pt = e.GetCurrentPoint(this);
        var pos = e.GetPosition(this);

        int hit = HitTest(pos, area);

        if (pt.Properties.IsRightButtonPressed)
        {
            if (hit >= 0) PointDeleteRequested?.Invoke(hit);
            return;
        }

        if (pt.Properties.IsLeftButtonPressed)
        {
            if (hit >= 0)
            {
                _dragIndex = hit;
                SetSelected(hit);
                e.Pointer.Capture(this);
            }
            else if (e.ClickCount == 2)
            {
                double f = PxToX(pos.X, area);
                double g = PxToY(pos.Y, area);
                PointAddRequested?.Invoke(Math.Clamp(f, XMin, XMax), Math.Clamp(g, YMin, YMax));
            }
            else
            {
                SetSelected(-1);
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragIndex < 0 || _dragIndex >= _points.Count) return;

        var area = PlotArea();
        var pos = e.GetPosition(this);
        double f = PxToX(pos.X, area);
        double g = PxToY(pos.Y, area);

        // conserver l'ordre des fréquences (bornes = voisins)
        double minF = _dragIndex > 0 ? _points[_dragIndex - 1].Freq * 1.02 : XMin;
        double maxF = _dragIndex < _points.Count - 1 ? _points[_dragIndex + 1].Freq * 0.98 : XMax;
        if (maxF < minF) { double mid = (minF + maxF) / 2; minF = maxF = mid; }
        f = Math.Clamp(f, minF, maxF);
        g = Math.Clamp(g, YMin, YMax);

        _points[_dragIndex] = new CurvePoint(f, g);
        if (Series.Count > 0) Series[0].Points = new List<CurvePoint>(_points);
        InvalidateVisual();
        PointDragged?.Invoke(_dragIndex, f, g);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragIndex = -1;
    }

    private void SetSelected(int index)
    {
        if (_selectedIndex == index) return;
        _selectedIndex = index;
        InvalidateVisual();
        SelectionChanged?.Invoke(index);
    }

    private int HitTest(Point pos, Rect area)
    {
        double best = 14 * 14; // tolérance en pixels²
        int idx = -1;
        for (int i = 0; i < _points.Count; i++)
        {
            double dx = XToPx(_points[i].Freq, area) - pos.X;
            double dy = YToPx(_points[i].Db, area) - pos.Y;
            double d2 = dx * dx + dy * dy;
            if (d2 < best) { best = d2; idx = i; }
        }
        return idx;
    }

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);

        var area = PlotArea();
        for (int i = 0; i < _points.Count; i++)
        {
            double x = XToPx(_points[i].Freq, area);
            double y = YToPx(Clamp(_points[i].Db, YMin, YMax), area);
            bool sel = i == _selectedIndex;
            double r = sel ? 5.5 : 4;
            ctx.DrawEllipse(sel ? PointSelBrush : PointBrush, PointPen, new Point(x, y), r, r);
        }
    }
}
