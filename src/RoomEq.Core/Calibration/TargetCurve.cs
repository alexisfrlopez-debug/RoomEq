using RoomEq.Core.Dsp;

namespace RoomEq.Core.Calibration;

/// <summary>Formes de courbe cible proposées pour la correction acoustique.</summary>
public enum TargetType
{
    /// <summary>Réponse plate (0 dB partout).</summary>
    Flat,

    /// <summary>Réponse plate + shelf de graves (confort d'écoute domestique).</summary>
    BassShelf,

    /// <summary>Pente descendante constante depuis 1 kHz (cible « studio » type Harman in-room).</summary>
    StudioTilt
}

/// <summary>Évaluation d'une courbe cible à une fréquence donnée.</summary>
public static class TargetCurve
{
    public const double DefaultShelfDb = 4.0;
    public const double DefaultTiltDbPerOct = -1.0;

    private static readonly BiquadFilter ShelfFilter = CreateShelf();

    private static BiquadFilter CreateShelf()
    {
        // Low-shelf : plateau +shelfDb sous ~100 Hz, transition vers 0 dB vers 400 Hz
        var f = new BiquadFilter();
        f.Configure(FilterType.LowShelf, 48000.0, 105.0, DefaultShelfDb, 0.75);
        return f;
    }

    public static double EvaluateDb(double freq, TargetType type, double shelfDb = DefaultShelfDb, double tiltDbPerOct = DefaultTiltDbPerOct)
    {
        switch (type)
        {
            case TargetType.Flat:
                return 0.0;

            case TargetType.BassShelf:
                // Réponse du low-shelf recalibrée si shelfDb diffère de la valeur par défaut
                double baseDb = ShelfFilter.ResponseDb(freq, 48000.0);
                double scale = Math.Abs(DefaultShelfDb) < 1e-9 ? 1.0 : shelfDb / DefaultShelfDb;
                return baseDb * scale;

            case TargetType.StudioTilt:
                return tiltDbPerOct * Math.Log2(Math.Max(freq, 20.0) / 1000.0);

            default:
                return 0.0;
        }
    }

    public static List<CurvePoint> SampleCurve(double fMin, double fMax, int pointCount, TargetType type, double shelfDb = DefaultShelfDb, double tiltDbPerOct = DefaultTiltDbPerOct)
    {
        var pts = new List<CurvePoint>(pointCount);
        double lmin = Math.Log10(fMin), lmax = Math.Log10(fMax);
        for (int i = 0; i < pointCount; i++)
        {
            double f = Math.Pow(10.0, lmin + (lmax - lmin) * i / (pointCount - 1));
            pts.Add(new CurvePoint(f, EvaluateDb(f, type, shelfDb, tiltDbPerOct)));
        }
        return pts;
    }
}
