# I-AUD-09 — EQ système par APO (sans pilote, sans mode test)

- **Module** : M-AUD-09 · **Version de spec** : 1.2 (as-built v3.9.1, 2
  novembre 2026 — correctif activation « sortie sans FxProperties ») ·
  **Code** : `src/RoomEq.Audio/SystemApo/` + **natif** `natif/roomeq-apo/`
  (N-APO-01)
- **Analyse d'origine** : `docs/EQ-SYSTEME-APO.md` (pourquoi ce module
  existe : contourner l'exigence de signature Microsoft des pilotes noyau).
- **As-built v1.2** : retour terrain v3.9.0 — l'activation échouait sur les
  sorties dont le pilote ne crée PAS la sous-clé `FxProperties` (pilotes
  génériques Windows, ex. « Realtek(R) Audio ») : « Ce périphérique n'expose
  pas de slot d'effets système ». Correctifs : (f) les ÉCRITS du store
  CRÉENT la sous-clé manquante (`OpenFxForWrite` : l'endpoint lui-même
  n'est jamais créé, seule sa sous-clé d'effets l'est) — le moteur audio
  lit le contenu de la clé quel que soit son auteur (« if the FxProperties
  key is absent entirely, then no system effect APOs are used », É.
  Dechamps ; c'est le mécanisme standalone d'Equalizer APO, 10+ ans de
  terrain) ; (g) la sauvegarde mémorise si la clé EXISTAIT avant nous
  (`FxPropertiesExisted`, défaut vrai — les sauvegardes v3.7–v3.9 restent
  lisibles) et la désactivation SUPPRIME la clé que nous avons créée si
  elle est redevenue vide (restitution exacte ; les valeurs étrangères
  écrites entre-temps sont conservées) ; (h) tout accès registre passe par
  la vue **Registry64 explicite** (MMDevices et l'inscription COM vivent
  dans la vue native — un processus 32 bits serait redirigé vers
  WOW6432Node et ne verrait rien : ceinture de sécurité, l'appli étant
  publiée win-x64).
- **As-built v1.1** : contrat public respecté ; ajustements de
  réalisation — (a) l'élévation unique se fait par RELANCE de l'exécutable
  courant (`--apo-setup <verbe>`, `ApoSetupRunner` → `ApoSetupCommands`,
  verdict lu dans `setup-result.json`) et non par un binaire externe ;
  (b) les DLL sont déployées vers `%ProgramData%\RoomEq\apo\bin` avec ACL
  restrictive (lecture seule pour les utilisateurs standard — sécurité : une
  DLL chargée par audiodg ne doit pas être remplaçable par un compte non
  administrateur), la racine `apo` restant modifiable par Utilisateurs et
  `NT SERVICE\Audiosrv` (courbe active + journal) ; (c) le registrar gère
  aussi `PKEY_AudioEndpoint_Disable_SysFx` (sauvegardé puis mis à 0 si les
  effets sont désactivés) ; (d) `RepairAll` opère en process élevé via la
  même relance ; (e) le cœur natif ajoute un fondu enchaîné de 20 ms entre
  courbes (le son ne s'interrompt jamais) et un cimetière de chaînes pour
  que le chemin temps réel ne libère JAMAIS de mémoire.

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
   exactement l'origine (ou supprime la valeur si elle n'existait pas ;
   ou supprime la sous-clé FxProperties si NOUS l'avons créée et qu'elle
   est redevenue vide) ; toutes les opérations sont idempotentes ;
   restaurer un endpoint jamais modifié = succès sans effet.
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

- Registrar (faux registre — `ApoRegistrarTests`, 20 tests) : énumération ;
  `IsForbiddenName` (câble RoomEq, VB-CABLE, VoiceMeeter, LoopBeAudio —
  négatifs inclus) ; **ordre sauvegarde→écriture vérifié par trace** ;
  restauration exacte (valeur EFX + Disable_SysFx, présentes ou absentes à
  l'origine) ; idempotence (double enable = aucune nouvelle écriture,
  sauvegarde non réécrasée ; double disable = succès sans effet) ; réparation
  après écrasement simulé (+ compte exact, 0 au second passage) ; refus
  anti-boucle SANS AUCUNE écriture ; inertie Linux du store réel ;
  **v3.9.1 : activation sur sortie sans FxProperties (clé créée, existence
  sauvegardée AVANT écriture) ; désactivation supprime la clé créée
  redevenue vide ; la garde si le pilote y a ajouté ses valeurs ; une
  sauvegarde héritée (sans le champ) ne supprime JAMAIS la clé**.
- ApoConfigStore (`ApoConfigStoreTests`, 7 tests) : aller-retour courbe
  (bandes + préampli + bande désactivée + type Notch), révision croissante,
  bypass sans perte de courbe (et idempotent), écriture atomique observée
  (temp+renommage, aucun résidu, active.json seul), fichier absent/corrompu
  → null sans exception.
- Natif (roomeq_apo_tests, **112 vérifications, 0 échec**) : IIDs
  littérales, fabrique COM, QI multi-base (thunks), agrégation refusée,
  latence 0, propriétés d'enregistrement, pass-through sans courbe (bit à
  bit), BUFFER_SILENT/BUFFER_INVALID, parité de courbe bit à bit avec
  EqChain (tolérance 0), in-place, **0 allocation et 0 libération en régime
  établi** (compteurs new/delete globaux), réécriture identique = continuité
  échantillon par échantillon (aucun swap), changement de courbe = fondu
  enchaîné reproduit au demi-ULP puis parité stricte, bypass transparent,
  config corrompue/supprimée = courbe conservée, Reset, 6 canaux (L/R
  traités, 4 autres intacts bit à bit) + mono 44,1 kHz (reconstruction de
  chaîne au reformatage), format non float = pass-through, déverrouillé =
  no-op sans écriture.
- Binaire livré (`build-apo.sh`, refuse le contraire) : PE32+ x86-64 et
  ARM64, DLL, exports `DllGetClassObject` + `DllCanUnloadNow`, dépendances
  système uniquement (ole32, kernel32, UCRT) — contrôlés par
  `scripts/check_pe_exports.py` (binutils-objdump ne lit pas le PE ARM64).
- Non-régression : suites xUnit 100 % vertes (483/483 : Core 237, Audio
  172, App 74) et harnais VST/AU/APO inchangés.
