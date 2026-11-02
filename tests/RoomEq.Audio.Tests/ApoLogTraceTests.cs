using RoomEq.Audio.SystemApo;
using Xunit;

namespace RoomEq.Audio.Tests;

/// <summary>
/// Tests de la classification du journal de l'APO (v3.9.9 — correctif du
/// faux « instanciée par le moteur audio » du verdict v3.9.8) : l'auto-test
/// pré-vol écrit les MÊMES lignes de cycle de vie que le moteur — seul le
/// marqueur de fin d'auto-test les départage, et l'instanciation AGRÉGÉE
/// (contrôleur COM non nul, le mécanisme réel d'audiodg) est la seule
/// preuve directe du moteur.
/// </summary>
public class ApoLogTraceTests
{
    private const string Marker = ApoLogTrace.SelfTestMarker;

    [Fact]
    public void JournalAbsent_OuVide_AucuneTrace()
    {
        Assert.Equal(ApoEngineTrace.None, ApoLogTrace.Classify(null));
        Assert.Equal(ApoEngineTrace.None, ApoLogTrace.Classify(""));
        Assert.False(ApoLogTrace.ShowsLoad(null));
    }

    [Fact]
    public void LignesDAutoTestSeules_NeComptentPlus()
    {
        // LE PIÈGE v3.9.8 : ces lignes viennent de l'auto-test du processus
        // élevé — avant le correctif, le verdict en déduisait « instanciée
        // par le moteur audio ». Après le marqueur : plus rien → None.
        string log = "APO instanciee (directe)\n"
                   + "LockForProcess : 48000 Hz, 2 canaux, conteneur 4 octets, float=1\n"
                   + "surveillant actif\n"
                   + "UnlockForProcess\n"
                   + "APO detruite\n"
                   + Marker + "\n";
        Assert.Equal(ApoEngineTrace.None, ApoLogTrace.Classify(log));
        Assert.False(ApoLogTrace.ShowsLoad(log));
    }

    [Fact]
    public void LignesApresLeMarqueur_Comptent()
    {
        string log = "APO instanciee (directe)\n"      // auto-test
                   + "UnlockForProcess\n"
                   + Marker + "\n"
                   + "APO instanciee (directe)\n";     // postérieur (moteur hypothétique)
        Assert.Equal(ApoEngineTrace.DirectOnly, ApoLogTrace.Classify(log));
        Assert.True(ApoLogTrace.ShowsLoad(log));
    }

    [Fact]
    public void InstanciationAgregeeApresMarqueur_PreuveDirecte()
    {
        string log = Marker + "\n"
                   + "APO instanciee (AGREGEE par le moteur audio)\n"
                   + "LockForProcess : 48000 Hz, 2 canaux, conteneur 4 octets, float=1\n";
        Assert.Equal(ApoEngineTrace.Aggregated, ApoLogTrace.Classify(log));
        Assert.True(ApoLogTrace.ShowsLoad(log));
    }

    [Fact]
    public void AgregeeAvantLeMarqueur_Seulement_NestPasUnePreuve()
    {
        // une ligne AGREGEE antérieure au DERNIER marqueur = un auto-test
        // précédent l'a écrite (ou journal ancien) : seul ce qui suit le
        // DERNIER marqueur compte
        string log = "APO instanciee (AGREGEE par le moteur audio)\n"
                   + Marker + "\n";
        Assert.Equal(ApoEngineTrace.None, ApoLogTrace.Classify(log));
    }

    [Fact]
    public void PlusieursMarqueurs_SeulLeDernierDelimite()
    {
        string log = "APO instanciee (directe)\n"
                   + Marker + "\n"
                   + "APO instanciee (directe)\n"       // entre les deux marqueurs
                   + Marker + "\n"
                   + "APO instanciee (AGREGEE par le moteur audio)\n"; // après le dernier
        Assert.Equal(ApoEngineTrace.Aggregated, ApoLogTrace.Classify(log));
    }

    [Fact]
    public void JournalSansMarqueur_ComportementHistorique()
    {
        // journal d'avant v3.9.9 (jamais marqué) : tout le texte compte —
        // rétrocompatibilité des journaux existants sur le terrain
        string log = "APO instancie (RoomEqApo)\n";     // orthographe d'avant v3.9.9
        Assert.Equal(ApoEngineTrace.DirectOnly, ApoLogTrace.Classify(log));
    }

    [Fact]
    public void JournalSansMarqueurMaisAvecAgregee_PreuveDirecte()
    {
        string log = "APO instanciee (AGREGEE par le moteur audio)\n";
        Assert.Equal(ApoEngineTrace.Aggregated, ApoLogTrace.Classify(log));
    }

    [Fact]
    public void LignesSansRapport_AucuneTrace()
    {
        string log = Marker + "\nrelecture : contenu identique (rev 3) : aucun swap\n";
        Assert.Equal(ApoEngineTrace.None, ApoLogTrace.Classify(log));
    }
}
