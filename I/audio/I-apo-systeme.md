# I-AUD-09 — EQ système par APO (sans pilote, sans mode test)

- **Module** : M-AUD-09 · **Version de spec** : 1 (proposition session 19 —
  décision D24 en attente de validation) · **Code** : futur — vague 9
  `src/RoomEq.Audio/SystemApo/` + **natif** `natif/roomeq-apo/` (N-APO-01)
- **Analyse d'origine** : `docs/EQ-SYSTEME-APO.md` (pourquoi ce module
  existe : contourner l'exigence de signature Microsoft des pilotes noyau).

## Intention

Faire traverser **tout le son d'une sortie Windows** par l'égaliseur principal
de RoomEq **sans installer de pilote** : un Audio Processing Object (APO)
user-mode `RoomEqApo.dll` est chargé par le moteur audio Windows
(`audiodg.exe`) sur les endpoints render choisis (position EFX), applique la
courbe de l'onglet Égaliseur en temps réel, et se pilote depuis l'application
(activation par sortie, courbe partagée, bypass pendant la mesure). Aucun
mode test, aucune signature Microsoft, aucune dépendance tierce ; installation
réversible en une élévation UAC. Le pilote noyau `roomeqvad` (I-INS-05) reste
une option avancée indépendante ; le pont ASIO (I-AUD-04) et la boucle
WASAPI (I-AUD-03) sont inchangés.

## Contrat public (extraits)

```csharp
namespace RoomEq.Audio.SystemApo;

// ---- État APO d'un endpoint render -------------------------------
public sealed record ApoEndpointInfo(
    string Id,                    // GUID endpoint MMDevice
    string FriendlyName,
    bool ApoEnabled,              // notre CLSID est dans FxProperties (EFX)
    bool OriginalSaved,           // valeur EFX d'origine sauvegardée
    bool RepairApplied);          // réécrit au dernier passage (MAJ pilote)

// ---- Registre APO (logique pure testable sur faux registre) ------
public sealed class ApoRegistrar
{
    // Windows + DLL RoomEqApo.dll déployée (Program Files) ; faux sinon
    static bool IsSupported { get; }

    IReadOnlyList<ApoEndpointInfo> EnumerateRenderEndpoints();

    // Anti-boucle (cohérent I-AUD-03 v2) : jamais sur le render du câble
    // virtuel RoomEq ni sur une sortie captée par une piste de la console.
    static bool IsForbiddenEndpoint(string friendlyName);

    // Active l'EQ système sur un endpoint : élévation UAC UNIQUE, puis
    // (1) sauvegarde de la valeur EFX d'origine, (2) écriture de notre
    // CLSID en FxProperties. Échec = ApoInstallResult(false, message),
    // jamais d'exception ; l'état d'origine reste utilisable.
    Task<ApoInstallResult> EnableAsync(string endpointId, CancellationToken ct);

    // Restaure la valeur EFX d'origine (ou supprime si absente à l'origine).
    Task<ApoInstallResult> DisableAsync(string endpointId, CancellationToken ct);

    // Re-vérifie les endpoints activés (une MAJ de pilote peut écraser
    // FxProperties) ; retourne le nombre de réparations effectuées.
    int RepairAll();

    // Désinstallation totale : restaure TOUS les endpoints, retire
    // l'inscription COM et le dossier ProgramData.
    Task<ApoInstallResult> UninstallAllAsync(CancellationToken ct);
}

public sealed record ApoInstallResult(bool Success, string Message);

// ---- Courbe active partagée avec l'APO ---------------------------
// %ProgramData%\RoomEq\apo\active.json (audiodg tourne en LocalService :
// PAS d'accès aux profils utilisateurs). Même format JSON que les presets
// de l'application (roomeq::preset, D23). Écriture atomique (temp+rename).
public sealed class ApoConfigStore(string? directoryOverride = null)
{
    static string DefaultDirectory { get; }   // %ProgramData%\RoomEq\apo

    void WriteCurve(IReadOnlyList<EqBand> bands, double preampDb);
    bool Bypass { get; }                      // true = l'APO laisse passer
    void SetBypass(bool bypass);              // activé autour de toute mesure
}
```

```text
Natif N-APO-01 — natif/roomeq-apo/ : RoomEqApo.dll (COM, x64 + ARM64)
  - CLSID unique RoomEq, IAudioSystemEffects + IAudioProcessingObject{,RT,
    Configuration} ; position EFX uniquement ({d04e05a6-…d1d},7).
  - Cœur DSP réutilisé du cœur partagé D23 (natif/roomeq-au/core/roomeq/dsp
    : biquad, eq_chain) — zéro nouveau DSP.
  - Thread de fond : poll mtime d'active.json (>= 100 ms) ; rebuild de la
    chaîne hors du chemin audio ; publication par pointeur atomique.
  - APOProcess : aucune allocation, aucun verrou, aucune I/O ; config
    illisible ou absente = pass-through transparent (son jamais coupé).
  - Harnais roomeq_apo_host (pattern du mini-hôte VST) : instanciation COM,
    LockForProcess/APOProcess, parité de courbe avec RoomEq.Core, continuité
    d'échantillons au rechargement, compteur d'allocations (0 en régime
    établi), canaux > 2 (L/R traités, autres intacts).
```

## Invariants

1. **Zéro pilote noyau** : le module ne touche jamais au magasin de pilotes,
   à bcdedit ni à un service ; il ne modifie que (a) l'inscription COM de
   notre DLL et (b) les valeurs FxProperties des endpoints explicitement
   choisis par l'utilisateur.
2. **Sauvegarde AVANT écriture** : aucune valeur EFX n'est écrasée sans
   copie préalable (registre + fichier) ; `Disable` restaure
   exactement l'origine (ou supprime la valeur si elle n'existait pas) ;
   toutes les opérations sont idempotentes ; restaurer un endpoint jamais
   modifié = succès sans effet.
3. **Anti-boucle** (ordre SÉCURITÉ, cohérent I-AUD-03 v2) : l'EQ système
   refuse tout endpoint virtuel RoomEq (« RoomEq Input ») et toute sortie
   actuellement captée par une piste de la console — le refus précède toute
   modification, message actionnable.
4. **Consentement + élévation explicites** : `Enable`/`Disable`/`Uninstall`
   déclenchent une invite UAC ; aucun effet de bord avant accord ; refus de
   l'invite = succès-faux lisible, rien modifié (jamais d'exception).
5. **RT-safety de l'APO** : `APOProcess` n'alloue pas, ne verrouille pas, ne
   touche ni fichier ni registre ; rechargement exclusivement par le thread
   de fond + swap atomique ; la couture entre deux chaînes est continue
   échantillon par échantillon (test).
6. **Localisation des données** : toute donnée lue par l'APO vit sous
   `%ProgramData%\RoomEq\apo\` (audiodg = LocalService, sans accès aux
   profils) ; ACL : lecture pour Users, écriture par l'application.
7. **Position EFX seule** ; l'activation suspend les effets EFX du fabricant
   sur cet endpoint (sauvegardés, restaurables) — l'UI le dit explicitement,
   la désactivation les restitue.
8. **Auto-réparation** : au démarrage de l'application et à chaque
   `DevicesChanged` (anti-rebond 1 s, I-AUD-03), les endpoints activés sont
   re-vérifiés et re-écrits si une MAJ de pilote a écrasé FxProperties ;
   le compte de réparations est journalisé (DiagnosticsLog).
9. **Bypass mesure** : toute mesure de calibration (balayage) force
   `SetBypass(true)` AVANT émission et restaure l'état précédent après —
   on mesure la pièce, pas la correction.
10. **Portabilité CI** : logique pure (énumération, sérialisation,
    sauvegarde/restauration, réparation) testée sous Linux sur un faux
    registre/faux système de fichiers ; le module est inerte hors Windows
    (listes vides, `IsSupported` faux), jamais d'exception.
11. **Désinstallation propre** : la désinstallation de l'application RESTAURE
    d'abord tous les endpoints modifiés, puis retire DLL + inscription COM +
    dossier ProgramData — un utilisateur qui désinstalle retrouve Windows
    exactement comme avant (audit de registre à l'appui).
12. **Vérité produit** : les limites documentées (mode exclusif/ASIO hors
    périmètre, effets fabricant suspendus, DRM à confirmer au terrain) sont
    énoncées dans l'UI et la doc utilisateur, jamais cachées.

## Critères de test

- Registrar (faux registre) : énumération ; `IsForbiddenEndpoint` (câble
  RoomEq, sortie captée) ; ordre sauvegarde→écriture vérifié par trace ;
  restauration exacte (valeur présente/absente à l'origine) ; idempotence
  (double enable, double disable) ; réparation après écrasement simulé ;
  refus UAC = rien modifié ; inertie Linux.
- ApoConfigStore : aller-retour courbe (bandes + préampli, format preset
  D23), bypass activé/désactivé, écriture atomique observée (temp+rename),
  répertoire override pour les tests.
- Natif (roomeq_apo_host) : instanciation COM du CLSID depuis un hôte
  extérieur ; blocs traités ; parité de courbe avec RoomEq.Core sur signal
  test (tolérance 0) ; rechargement de config sans discontinuité
  (continuité échantillon par échantillon à la couture) ; pass-through
  (config absente/corrompue/bypass) ; **0 allocation dans APOProcess** en
  régime établi (compteur instrumenté) ; multi-canaux (L/R traités, autres
  intacts) ; build croisé x64 vérifié (PE64, exports COM).
- Non-régression : suites xUnit existantes 100 % vertes (CI Linux).
