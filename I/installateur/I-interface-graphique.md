# I-INS-08 — Interface graphique et couche d'affichage

- **Module** : M-INS-08 · **Version de spec** : 1 · **Code** : futur — vague 8
  (`src/RoomEq.Installer/UiSink.cs` + `Gui/`)
- **Référence scellée** : `roomeq-git-ref/installer/driver-setup/Ui.cs`,
  `GuiRunner.cs`, `Program.cs` (Main/AttachToConsole, v2.2.0–v2.2.2).

## Intention

Séparer STRICTEMENT la logique d'installation (M-INS-01..07) de son
affichage : la logique écrit dans un puits de sortie abstrait, la console et
la fenêtre graphique ne sont que deux branchemles du même puits. Le
double-clic ouvre la fenêtre — **zéro ligne de commande** pour
l'utilisateur final ; les options console restent pour le support et
l'automatisation.

## Contrat public — le puits d'affichage

```csharp
namespace RoomEq.Installer;

/// Puits de sortie rebranchable : la séquence ne connaît NI la console
/// NI la GUI — uniquement ces cinq opérations.
public interface UiSink
{
    void Say(string line);                 // ligne de journal ordinaire
    void Heading(string title);            // titre de section (RESULTAT, VERDICT)
    void SayError(string line);            // ligne [ERREUR] (rouge en GUI)
    bool Confirm(string question, bool defaultYes);  // question fermée
    void Pause();                          // attente console ; no-op en GUI
}

public sealed class ConsoleUi : UiSink { … }   // comportement historique
public sealed class GuiUi : UiSink { … }       // journal + dialogues (vague 8)
```

## Modes de lancement (contrat CLI inchangé)

| Invocation | Comportement |
|---|---|
| *(aucun argument)* | **fenêtre graphique**, aucune action lancée |
| `--gui [--install]` | fenêtre + action automatique (utilisée par le bouton « Installer le pilote » de l'application) |
| `--console` (`--c`) | installation console (défaut historique des options) |
| `--status` (`--s`) | état + VERDICT (rien modifié) |
| `--uninstall` (`--u`) | retrait complet |
| `--mode-normal` (`--n`, `--normal`) | retour au mode normal |
| `--install --accept-testmode` / `--no-testmode` | automatisation du consentement (M-INS-05) |
| argument inconnu | usage + exit 1 |

## Contrat de la fenêtre graphique

1. **Titre** : « RoomEq — Pilote audio virtuel ».
2. **Panneau d'état** (haut) : instantané des sondes (`StatusSnap`) —
   Secure Boot, mode test, build du pilote, paquet magasin (oemNN.inf),
   périphérique (+ démarré / problème + code), service `roomeqvad`,
   ASIO (chemin enregistré) — bouton **Actualiser**.
3. **Journal** (milieu, rempli) : les `Say`/`Heading`/`SayError` de la
   séquence, dans l'ordre, jamais tronqués.
4. **Boutons d'action** : `Installer le pilote…` (accentué, par défaut),
   `Diagnostic complet`, `Revenir au mode normal`, `Désinstaller…`
   (**en rouge**), + `Fermer`.
5. Pendant une action : désactivation des boutons, libellé « Action en
   cours — attendez la fin… » ; à la fin, actualisation de l'état.
6. **Threading** : chaque action tourne sur un thread de fond ; journal et
   dialogues passent par le marshaling UI — l'interface ne gèle JAMAIS.
   Les exceptions non gérées (thread UI et global) donnent une boîte
   d'erreur, pas un crash silencieux.

## Invariants

1. **Séparation stricte** : aucun `Console.*` ni contrôle graphique dans
   M-INS-01..07 — la logique est testable avec un `UiSink` factice.
2. **Textes de logique en ASCII** (robustesse des consoles héritées :
   pages de code OEM) ; l'habillage seul (boutons, titres, libellés)
   utilise l'accentué. Les `Confirm` transportent le défaut OUI/NON.
3. **Exécutable WinExe** : les options console s'attachent à la console
   parente (`AttachConsole(AttachParentProcess)`) sinon allouent une
   console (le rapport reste visible au double-clic avec arguments).
4. **Élévation unique** : manifest `requireAdministrator` — une seule
   invite UAC pour tout le programme ; lancement non élevé → message de
   conduite (clic droit → exécuter en tant qu'administrateur) + exit 1,
   jamais de tentative silencieuse de relance.
5. `Heading`/`SayError` NULL-safe : utilisables avant tout rebranchement
   (repli sur `Say`) — la logique ne doit jamais vérifier l'existence du
   puits graphique.

## Critères de test

- la séquence complète s'exécute sur un `UiSink` factice : ordre exact des
  Say/Heading/SayError, Confirm appelé avec le bon défaut, aucun appel
  console ni graphique direct (assertion par absence de référence) ;
- parsing des arguments : chaque forme du tableau (y compris raccourcis
  `-i`, `-s`, `-u`, `-n`) → bon mode ; inconnu → usage + exit 1 ;
- aucun argument → mode GUI ; `--gui --install` → GUI + action install ;
- `StatusSnap` : les 8 sondes présentes, sondes en échec = valeurs de
  repli (indéterminé/absent), jamais d'exception ;
- non élevé → exit 1 après le message (mock du rôle) ;
- textes de logique : 100 % ASCII (test de balayage des chaînes émises).
