using RoomEq.Core.Dsp;

namespace RoomEq.Core.Calibration;

/// <summary>Formes de courbe cible proposées pour la correction acoustique — spec I-CAL-01.</summary>
public enum TargetType
{
    /// <summary>Réponse plate (0 dB partout).</summary>
    Flat,

    /// <summary>Réponse plate + shelf de graves (confort d'écoute domestique).</summary>
    BassShelf,

    /// <summary>Pente descendante constante depuis 1 kHz (cible « studio » type Harman in-room).</summary>
    StudioTilt
}

/// <summary>Évaluation d'une courbe cible à une fréquence donnée — spec I-CAL-01.</summary>
public static class TargetCurve
{
    public const double DefaultShelfDb = 4.0;
    public const double DefaultTiltDbPerOct = -1.0;

    /// <summary>Low-shelf de référence : plateau sous ~100 Hz, transition 0 dB vers 400 Hz.</summary>
    private static readonly BiquadFilter ShelfFilter = CreateShelf();

    private static BiquadFilter CreateShelf()
    {
        var f = new BiquadFilter();
        f.Configure(FilterType.LowShelf, 48000.0, 105.0, DefaultShelfDb, 0.75);
        return f;
    }

    /// <summary>Évalue la cible (dB) à la fréquence donnée.</summary>
    public static double EvaluateDb(double freq, TargetType type,
        double shelfDb = DefaultShelfDb, double tiltDbPerOct = DefaultTiltDbPerOct)
    {
        switch (type)
        {
            case TargetType.Flat:
                return 0.0;

            case TargetType.BassShelf:
                // réponse du low-shelf recalibrée linéairement si shelfDb diffère du défaut
                double baseDb = ShelfFilter.ResponseDb(freq);
                double scale = Math.Abs(DefaultShelfDb) < 1e-9 ? 1.0 : shelfDb / DefaultShelfDb;
                return baseDb * scale;

            case TargetType.StudioTilt:
                return tiltDbPerOct * Math.Log2(Math.Max(freq, 20.0) / 1000.0);

            default:
                return 0.0;
        }
    }

    /// <summary>Échantillonne la cible sur des points log-répartis (bornes incluses).</summary>
    public static List<CurvePoint> SampleCurve(double fMin, double fMax, int pointCount, TargetType type,
        double shelfDb = DefaultShelfDb, double tiltDbPerOct = DefaultTiltDbPerOct)
    {
        if (pointCount < 2)
            throw new ArgumentOutOfRangeException(nameof(pointCount), "Au moins deux points sont requis.");
        if (fMin <= 0 || fMax <= fMin)
            throw new ArgumentException("Il faut 0 < fMin < fMax.");

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
