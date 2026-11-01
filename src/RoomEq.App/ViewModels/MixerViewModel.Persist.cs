using CommunityToolkit.Mvvm.ComponentModel;
using RoomEq.Audio.Routing;

namespace RoomEq.App.ViewModels;

/// <summary>
/// MixerViewModel — persistance mixer.json (v3.9.0, D26) : la console a été
/// retirée, il ne reste à mémoriser que la sortie d'écoute (barre du haut),
/// l'option « preset auto par sortie » et les défauts Windows sauvegardés par
/// l'ANCIEN routage (migration D25). Les tranches, faders, EQ de piste et
/// options de routage des versions antérieures sont LUS pour tolérance
/// (jamais d'échec de chargement) puis cessent d'être écrits.
/// La persistance ne lève jamais (l'audio prime) — spec I-APP-03 v3.
/// </summary>
public partial class MixerViewModel
{
    public void SaveConfig()
    {
        try
        {
            var stored = new StoredMixer
            {
                MasterOutputId = SelectedOutput?.Id,
                AutoOutputPreset = AutoOutputPreset,
                SavedDefaultRenderId = _savedRenderId,
                SavedDefaultCaptureId = _savedCaptureId
            };
            _store.Save(stored);
        }
        catch
        {
            // la persistance ne doit jamais perturber l'audio
        }
    }

    private void LoadConfig()
    {
        var cfg = _store.Load();
        AutoOutputPreset = cfg.AutoOutputPreset;
        _savedRenderId = cfg.SavedDefaultRenderId;
        _savedCaptureId = cfg.SavedDefaultCaptureId;
        _storedOutputId = cfg.MasterOutputId;
    }
}
