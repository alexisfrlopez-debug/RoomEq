# I-INS-01 — Séquence d'installation (conducteur)

- **Module** : M-INS-01 · **Version de spec** : 1 · **Code** : futur — vague 8
  (`src/RoomEq.Installer/InstallSequence.cs`)
- **Référence scellée** : `roomeq-git-ref/installer/driver-setup/Program.cs`
  (RunInstall, v2.2.2) + `RoomEq.Installer.CA/InstallActions.cs` (MSI)

> Spec écrite AVANT le code (R2) par rétro-ingénierie du comportement v2.2.2
> validé sur machine réelle. Le binaire scellé reste la référence exploitable
> jusqu'au recodage (D1/D11) ; cette spec est le contrat que le recodage doit
> reproduire, améliorer, et surtout verrouiller par des tests.

## Intention

Orchestrer l'installation complète de la carte son virtuelle RoomEq (pilote +
périphérique + ASIO) en une séquence **numérotée, idempotente et
auto-rapportée**. Chaque étape affiche `[n/6] <titre>` puis son résultat ;
un échec d'étape critique arrête proprement avec un message standardisé, sans
jamais laisser la machine dans un état cassé. La même logique alimente la
console et la fenêtre graphique via la couche d'affichage (M-INS-08) — elle
n'est jamais couplée à l'un des deux.

## Contrat public

```csharp
namespace RoomEq.Installer;

/// Résultat d'une exécution de séquence (install / uninstall / mode-normal).
public sealed record SequenceResult(int ExitCode, bool RebootNeeded, string Verdict);

public sealed class InstallSequence(UiSink ui, ToolContract tools, Win32Pnp pnp)
{
    /// Installation complète : consentement → payload → certificat →
    /// testsigning → magasin → périphérique → ASIO → verdict → reboot.
    public SequenceResult Install(InstallOptions o);

    /// Diagnostic sans aucune modification : sondes + VERDICT orienté cause.
    public SequenceResult Status();

    /// Retrait complet (cf. M-INS-06).
    public SequenceResult Uninstall();

    /// Retour au mode normal Windows (cf. M-INS-06).
    public SequenceResult ModeNormal();
}

public sealed record InstallOptions(
    bool AcceptTestMode = false,   // --accept-testmode (automatisation)
    bool NoTestMode = false);      // --no-testmode (annulation propre)
```

## Étapes d'installation (ordre IMMUABLE)

| n | Étape | Échec critique ? | Notes |
|---|-------|------------------|-------|
| 0 | Consentement mode test | — | cf. M-INS-05 ; AVANT toute modification |
| 1 | Extraction du payload embarqué | oui | `%TEMP%\RoomEqDriverSetup` (M-INS-07) |
| 2 | Certificat de test | oui | **[SAUT]** si aucun `.cer` embarqué (build signé officiellement) |
| 3 | Mode testsigning | oui | **[SAUT]** si déjà actif ou build officiel (M-INS-05) |
| 4 | `pnputil /add-driver … /install` | oui | code 259 toléré (paquet ajouté, pas encore de périphérique) |
| 5 | Périphérique `ROOT\ROOMEQVAD` + liaison pilote | oui* | recette M-INS-02 ; *si reboot requis (étape 3 vient d'activer le mode test), la liaison différée au redémarrage est tolérée et signalée |
| 6 | DLL ASIO + enregistrement COM | **non** | WASAPI fonctionne sans ASIO ; message « re-essayer plus tard, sans risque » |

## Invariants

1. **Étape 0 avant tout** : aucune modification système (certificat, pilote,
   mode, registre) avant la réponse explicite de l'utilisateur.
2. **Idempotence** : relancer `--install` après une installation partielle ou
   complète est sans risque et répare (étape 5 détecte le périphérique déjà
   présent : « déjà présent, pilote lié »).
3. **Modèle d'échec standardisé** : `FailStep(message)` = `[ERREUR]` + une
   ligne + rappel « Rien n'a été cassé : les étapes précédentes sont
   inoffensives (certificat uniquement, ou paquet ajouté sans liaison) » +
   consigne de transmission du journal + code retour 1.
4. **Verdict final** : « installation terminée » + où les cartes apparaissent
   après redémarrage ; si le mode test vient d'être activé : bandeau
   `REDEMARRAGE REQUIS` + explication du filigrane attendu.
5. **Redémarrage proposé, jamais imposé** : question à défaut OUI, puis
   `shutdown /r /t 5` ; refus = consigne de redémarrage manuel.
6. **Sorties de code** : 0 = succès ou annulation propre (refus du consentement
   = 0, rien modifié) ; 1 = échec d'étape critique.
7. Le diagnostic (`Status`) ne modifie JAMAIS rien et se termine par un
   VERDICT orienté cause (cf. § Critères).

## Critères de test

- séquence : mock des outils/pnP → ordre des étapes exactement 1→6, avec
  `[n/6]` émis pour chaque ;
- idempotence : deuxième appel = « déjà présent, pilote lié », pas d'erreur ;
- échec étape 5 SANS reboot requis → exit 1, message standard « Rien n'a été
  cassé » ;
- échec étape 5 AVEC reboot requis → toléré, verdict « liaison différée » ;
- échec ASIO (étape 6) → exit 0 quand même, avertissement ;
- refus consentement → exit 0, aucun appel système mutant effectué
  (assertion sur les mocks) ;
- `--no-testmode` → exit 0 sans aucun appel système mutant ;
- build sans `.cer` → étapes 2 et 3 émettent `[SAUT]`, pas de reboot proposé ;
- VERDICT du status : les six cas (non installé / magasin seul / mode test
  manquant / Secure Boot ignoré / problème PnP / tout correct) produisent
  chacun leur texte orienté cause.
