using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoomEq.Audio.Playback;
using RoomEq.Core.Io;

namespace RoomEq.App.ViewModels;

/// <summary>
/// Choix du menu déroulant des signaux de test : soit un signal généré à la
/// volée par le moteur (bruit, sinus, balayage — voir TestSignalCatalog), soit
/// un fichier WAV téléchargé depuis le catalogue officiel du projet.
/// </summary>
public sealed record TestSignalChoice(
    string Id,
    string DisplayName,
    string Category,
    GeneratorSignalItem? Generator,
    string? FilePath);

/// <summary>
/// Menu des signaux de test de la barre supérieure (extrait de MainViewModel
/// en v3 pour respecter la taille cible) : signaux intégrés + bibliothèque
/// téléchargée, lecture du choix courant — spec I-APP-01.
/// </summary>
public partial class TestSignalMenu : ObservableObject
{
    private readonly PlaybackEngine _engine;
    private readonly SignalStore _store;
    private readonly Action<string>? _setStatus;

    /// <summary>Menu déroulant des signaux de test (intégrés + fichiers téléchargés).</summary>
    public ObservableCollection<TestSignalChoice> SignalChoices { get; } = new();

    [ObservableProperty] private TestSignalChoice? selectedSignal;

    /// <summary>Magasin et statut injectables pour les tests.</summary>
    public TestSignalMenu(PlaybackEngine engine, SignalStore? store = null, Action<string>? setStatus = null)
    {
        _engine = engine;
        _store = store ?? new SignalStore();
        _setStatus = setStatus;
        Refresh();
    }

    /// <summary>Reconstruit le menu : signaux intégrés + bibliothèque téléchargée.</summary>
    public void Refresh()
    {
        TestSignalChoice? previous = SelectedSignal;
        SignalChoices.Clear();
        foreach (var g in TestSignalCatalog.BuiltIn)
            SignalChoices.Add(new TestSignalChoice(g.Id, g.DisplayName, g.Category, g, null));

        foreach (var f in _store.List())
            SignalChoices.Add(new TestSignalChoice(
                "fichier|" + f.FileName, f.DisplayName, "Téléchargés", null, _store.PathFor(f.FileName)));

        // conserve la sélection précédente si elle existe encore, sinon le signal
        // par défaut (bruit rose : réglage de niveau d'écoute)
        SelectedSignal = (previous != null ? SignalChoices.FirstOrDefault(c => c.Id == previous.Id) : null)
                         ?? SignalChoices.FirstOrDefault(c => c.Id == TestSignalCatalog.DefaultItemId)
                         ?? SignalChoices.FirstOrDefault();
    }

    [RelayCommand]
    private void PlaySelected()
    {
        var choice = SelectedSignal;
        if (choice == null) return;

        if (choice.Generator is { } g)
        {
            _engine.PlayTestSignal(g.Signal, g.Amplitude, g.SineFrequency, g.SweepSpec);
            _setStatus?.Invoke(g.DisplayName + " — ajustez le volume si besoin.");
            return;
        }

        if (choice.FilePath != null)
        {
            try
            {
                // les fichiers du catalogue sont courts : lecture en boucle pour
                // un usage à l'oreille continu, comme les signaux générés
                var reader = new NAudio.Wave.AudioFileReader(choice.FilePath);
                void Restart() { try { reader.Position = 0; } catch { /* fichier retiré ? */ } }
                _engine.PlaySource(new LoopingSampleProvider(reader, Restart) { Loop = true },
                    choice.DisplayName, Restart);
                _setStatus?.Invoke(choice.DisplayName + " (fichier téléchargé, lecture en boucle).");
            }
            catch (Exception ex)
            {
                _setStatus?.Invoke("Lecture impossible : " + ex.Message);
                DiagnosticsLog.Error("Lecture d'un signal téléchargé : " + ex.Message);
            }
        }
    }
}
