using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoomEq.Core.Io;

namespace RoomEq.App.ViewModels;

/// <summary>
/// Partie « export FabFilter Pro-Q 3 » de l'onglet Égaliseur (spec
/// I-APP-04 § export) : vérifie l'exportabilité du preset courant, laisse
/// la vue ouvrir le dialogue d'enregistrement (StorageProvider) puis encode
/// le fichier .ffp via <see cref="FabFilterProQ3"/> (spec I-IO-06).
/// </summary>
public partial class EqViewModel
{
    /// <summary>Statut de l'export .ffp (confirmation avec chemin, ou erreur actionnable).</summary>
    [ObservableProperty] private string exportStatus = "";

    /// <summary>Demande l'ouverture du dialogue d'enregistrement — abonné par EqTab.</summary>
    public event Action? FabFilterExportRequested;

    /// <summary>
    /// Vérifie que le preset courant est exportable (≤ 24 bandes actives) puis
    /// demande le dialogue à la vue ; le statut guide l'utilisateur sinon.
    /// </summary>
    [RelayCommand]
    private void ExportFabFilter()
    {
        var current = BuildPreset();
        int active = current.Bands.Count(b => b.Enabled);
        if (active > FabFilterProQ3.MaxBands)
        {
            ExportStatus = $"Export impossible : {active} bandes actives, FabFilter Pro-Q 3 en accepte " +
                $"au maximum {FabFilterProQ3.MaxBands}. Désactivez ou supprimez des bandes " +
                "(ou réduisez la précision avant d'importer la calibration).";
            return;
        }
        FabFilterExportRequested?.Invoke();
    }

    /// <summary>Nom de fichier suggéré pour l'export (nom du preset assaini + .ffp).</summary>
    public string SuggestedFabFilterFileName() =>
        (string.IsNullOrWhiteSpace(PresetName) ? "preset" : AppPaths.SanitizeFileName(PresetName)) + ".ffp";

    /// <summary>Encode et écrit le preset courant en .ffp — appelé par la vue après le dialogue.</summary>
    public void SaveFabFilterExport(string path)
    {
        try
        {
            var preset = BuildPreset();
            FabFilterProQ3.Write(preset, path);
            int active = preset.Bands.Count(b => b.Enabled);
            ExportStatus = $"Preset exporté vers « {path} » — {active} bandes. Dans Pro-Q 3 : " +
                "bouton des presets ▸ Load…, ou glissez-déposez le fichier sur le greffon.";
        }
        catch (Exception ex)
        {
            ExportStatus = "Export impossible : " + ex.Message;
        }
    }
}
