namespace RoomEq.Core.Calibration;

/// <summary>
/// Niveau de précision d'une correction d'égalisation générée depuis la courbe de
/// calibration. La précision détermine le nombre de bandes, leur espacement minimal
/// et leurs largeurs (Q) — les fréquences et les gains restent optimisés
/// automatiquement par AutoBandPlanner pour la courbe mesurée.
/// </summary>
public enum CorrectionPrecision
{
    /// <summary>Correction douce : peu de bandes, larges et prudentes.</summary>
    Soft,

    /// <summary>Équilibre entre efficacité et naturel.</summary>
    Standard,

    /// <summary>Précision de calibration : anomalies moyennement larges corrigées.</summary>
    Fine,

    /// <summary>Précision maximale : bandes fines et nombreuses, traque les résonances étroites.</summary>
    Ultra
}

/// <summary>Une entrée affichable du catalogue de précisions.</summary>
public sealed record CorrectionPrecisionOption(CorrectionPrecision Kind, string Name, string Description);

public static class CorrectionPrecisionCatalog
{
    public static IReadOnlyList<CorrectionPrecisionOption> All { get; } = new[]
    {
        new CorrectionPrecisionOption(
            CorrectionPrecision.Soft, "Douce",
            "Peu de bandes (8 max), larges et douces : dégrossit la pièce sans toucher au caractère du système."),
        new CorrectionPrecisionOption(
            CorrectionPrecision.Standard, "Standard",
            "Jusqu'à 14 bandes : bon équilibre entre correction de la pièce et naturel de l'écoute."),
        new CorrectionPrecisionOption(
            CorrectionPrecision.Fine, "Fine",
            "Jusqu'à 20 bandes : corrige les anomalies moyennement larges — niveau recommandé après calibration."),
        new CorrectionPrecisionOption(
            CorrectionPrecision.Ultra, "Très fine",
            "Jusqu'à 30 bandes fines : traque les résonances étroites ; correction la plus fidèle au mesuré."),
    };

    public static CorrectionPrecisionOption Option(CorrectionPrecision precision)
        => All.First(o => o.Kind == precision);
}

/// <summary>
/// Traduit un niveau de précision en paramètres du planificateur automatique :
/// nombre maximal de bandes, seuil de correction, cible de résidu, plage de Q et
/// espacement minimal entre bandes (anti-doublons).
/// </summary>
public static class CorrectionPrecisionSettings
{
    public static AutoBandOptions ToOptions(
        CorrectionPrecision precision, CorrectionOptions opt, int sampleRate)
    {
        var o = AutoBandOptions.From(opt, sampleRate);
        switch (precision)
        {
            case CorrectionPrecision.Soft:
                o.MaxFilters = 8;
                o.ThresholdDb = 1.5;
                o.ResidualTargetRmsDb = 2.0;
                o.MinQ = 0.5;
                o.MaxQ = 4.0;
                o.MinSpacingOctaves = 1.0 / 6.0;
                o.SpacingFactor = 0.8;
                break;

            case CorrectionPrecision.Standard:
                o.MaxFilters = 14;
                o.ThresholdDb = 1.0;
                o.ResidualTargetRmsDb = 1.5;
                o.MinQ = 0.7;
                o.MaxQ = 6.0;
                o.MinSpacingOctaves = 1.0 / 12.0;
                o.SpacingFactor = 0.7;
                break;

            case CorrectionPrecision.Fine:
                o.MaxFilters = 20;
                o.ThresholdDb = 0.75;
                o.ResidualTargetRmsDb = 1.0;
                o.MinQ = 0.7;
                o.MaxQ = 8.0;
                o.MinSpacingOctaves = 1.0 / 12.0;
                o.SpacingFactor = 0.6;
                break;

            case CorrectionPrecision.Ultra:
                o.MaxFilters = 30;
                o.ThresholdDb = 0.5;
                o.ResidualTargetRmsDb = 0.75;
                o.MinQ = 1.0;
                o.MaxQ = 12.0;
                o.MinSpacingOctaves = 1.0 / 24.0;
                o.SpacingFactor = 0.5;
                break;
        }
        return o;
    }
}
