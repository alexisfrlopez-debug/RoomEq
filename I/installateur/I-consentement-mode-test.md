# I-INS-05 — Consentement du mode test et Secure Boot

- **Module** : M-INS-05 · **Version de spec** : 1 · **Code** : futur — vague 8
  (`src/RoomEq.Installer/TestModePolicy.cs`)
- **Référence scellée** : `roomeq-git-ref/installer/driver-setup/Program.cs`
  (bloc 0 de RunInstall, `EnableTestSigning`, `DisableTestSigningAndOfferReboot`,
  `IsSecureBootEnabled`, v2.2.2) — politique introduite en v2.1.0 après les
  incidents « l'installation est empêchée par Windows ».

## Intention

Porter la **politique de consentement** du mode test de Windows : le pilote
distribué est signé-test ; Windows ne peut le charger qu'en mode test
(bcdedit), signalé par un filigrane de bureau. Le programme ne bascule
JAMAIS Windows en mode test sans accord explicite de l'utilisateur — pas de
silence, pas de défaut permissif, pas de boîte pré-cochée. Cette politique
est un choix produit consigné (v2.1.0) et un engagement de sécurité : une
machine en mode test accepte n'importe quel pilote signé-test.

## Contrat public

```csharp
namespace RoomEq.Installer;

public enum TestModeDecision { Proceed, Refused, CancelledByOption }

public sealed class TestModePolicy(UiSink ui)
{
    /// Le build distribue-t-il un pilote signé-test ? (présence du .cer
    /// embarqué — cf. M-INS-07 ; faux = signature officielle, rien à faire)
    public static bool TestSignedBuild { get; }

    /// Étape 0 : question AVANT toute modification. Défaut = NON.
    public TestModeDecision AskConsent(InstallOptions o);

    /// Active le mode test (bcdedit). Échec → message cause + voies A/B.
    public bool EnableTestSigning(out string error);

    /// Coupe le mode test : /set off PUIS /deletevalue en secours.
    public bool DisableTestSigning();

    /// Sonde Secure Boot : true/false, null = indéterminé (jamais d'échec).
    public bool? IsSecureBootEnabled();
}
```

## Invariants

1. **Ordre absolu** : la question de consentement précède TOUT effet de bord
   (aucun certificat importé, aucun paquet ajouté, aucun mode changé avant
   la réponse). Le refus retourne 0 avec le message « rien n'a été modifié »
   et les trois sorties possibles rappelées (revenir plus tard, diagnostic,
   retour au mode normal).
2. **Défaut NON** : la question se pose avec réponse par défaut « non »,
   aussi bien en console (entrée vide = non) qu'en GUI (bouton proposé = non).
3. **`--no-testmode`** = annulation propre et silencieuse (exit 0, rien
   modifié) ; **`--accept-testmode`** = consentement préalable pour
   l'automatisation (le message explicatif reste affiché, la question sautée).
4. **Explication honnête avant la question** : ce qu'est le mode test, le
   filigrane attendu sur le bureau, ce qui se passe sans lui (la carte
   n'apparaît pas), et les **trois voies** : (A) accepter le mode test ;
   (B) attendre le pilote signé Microsoft (renvoi documentaire) ; (C)
   utiliser RoomEq SANS carte virtuelle (lecteur + EQ, calibration, boucle
   WASAPI restent fonctionnels) — la voie C n'est jamais présentée comme une
   défaillance.
5. **Pré-diagnostic Secure Boot AVANT bcdedit on** (WMI
   `root\Microsoft\Windows\SecureBoot`, `Win32_SecureBootStatus` ; échec de
   sonde = indéterminé, jamais bloquant) :
   - Secure Boot actif + `bcdedit /set testsigning on` réussit →
     avertissement « risque d'être ignoré au démarrage » + conduite à tenir ;
   - Secure Boot actif + bcdedit échoue → **cause confirmée** : Windows
     refuse par conception de charger un pilote signé-test avec Secure Boot ;
     deux solutions présentées : A. désactiver Secure Boot en BIOS/UEFI puis
     relancer (renvoi cas 5 de la doc d'installation), B. attendre le pilote
     signé Microsoft ;
   - Secure Boot indéterminé + bcdedit échoue → cause la plus fréquente
     citée (Secure Boot) + demande de transmission du journal.
6. **build officiellement signé** (aucun `.cer` embarqué) : étapes
   certificat/testsigning/reboot entièrement sautées — l'installation devient
   immédiate, sans question.
7. `DisableTestSigning` (retour au mode normal) : `/set testsigning off`,
   et si échec, `/deletevalue {current} testsigning` en secours ; l'échec
   des deux avec mode toujours actif = erreur (cause la plus fréquente
   rappelée : Secure Boot — le mode test n'a alors jamais pu être activé).

## Critères de test

- consentement refusé → AUCUN appel mutateur sur mocks, exit 0 ;
- défaut = NON (entrée vide console, focus bouton non en GUI factice) ;
- `--no-testmode` → annulation sans question, exit 0 ;
- `--accept-testmode` → pas de question, séquence continue jusqu'à bcdedit ;
- Secure Boot true + bcdedit on OK → avertissement présent, séquence continue ;
- Secure Boot true + bcdedit on KO → message « CAUSE CONFIRMÉE » + voies A/B,
  exit 1 ;
- sonde WMI en échec → indéterminé, pas d'exception, pas de blocage ;
- build sans .cer → aucune question posée, aucun bcdedit invoqué ;
- off + deletevalue : off échoue, deletevalue réussit → succès compté ;
  les deux échouent avec mode actif → erreur avec mention Secure Boot.
