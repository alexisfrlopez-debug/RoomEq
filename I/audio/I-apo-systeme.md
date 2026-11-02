# I-AUD-09 — EQ système par APO (sans pilote, sans mode test)

- **Module** : M-AUD-09 · **Version de spec** : 1.9 (as-built v3.9.8, 2
  novembre 2026 — position ADAPTÉE AU PÉRIPHÉRIQUE (MFX pour les
  Bluetooth « combinés » de Windows 11), AUTO-TEST COM PRÉ-VOL de la DLL
  avant toute écriture registre, verdict d'échec DIFFÉRENTIEL complet) ·
  **Code** : `src/RoomEq.Audio/SystemApo/` + **natif** `natif/roomeq-apo/`
  (N-APO-01)
- **Analyse d'origine** : `docs/EQ-SYSTEME-APO.md` (pourquoi ce module
  existe : contourner l'exigence de signature Microsoft des pilotes noyau).
- **As-built v1.9** : retour terrain v3.9.7 — capture ENFIN transmise
  (« Échec de l'activation de 'Casque JBL Tune Flex 2' — LE SON A ÉTÉ
  PRÉSERVÉ : le moteur audio n'a pas réussi à charger l'égaliseur ») :
  la sonde et le rollback ont fonctionné comme prévu, mais le verdict ne
  contenait AUCUN élément différentiel et le journal de l'APO restait
  vide (la DLL n'est toujours jamais instanciée par audiodg). FAIT
  ÉTABLI (source d'Equalizer APO LUE INTÉGRALEMENT, `DeviceAPOInfo.cpp`) :
  « bluetooth devices may be combined in Windows 11, EFX will not work
  then » — un endpoint qui porte la propriété « périphérique combiné »
  (`{b3f8fa53-0004-438e-9003-51a46e139bfc},41` dans sa sous-clé
  \Properties — cas des Bluetooth sous Windows 11, dont le casque du
  retour terrain) ne doit PAS recevoir d'effet en position EFX : la
  référence installe en SFX/MFX. Croisements : ticket Equalizer APO #77
  (PIDs complets SFX ,5 / MFX ,6 / EFX ,7 / multi ,13-15 ; « If Multiple
  APO CLSID exist, Windows will ignore Single APO CLSID ») et notes
  É. Dechamps (« If any of SFX, MFX or EFX are present, then LFX and GFX
  are ignored »). Correctifs v3.9.8 : (af) **position adaptée au
  périphérique** — `ApoSlotPolicy` (PUR) choisit EFX par défaut, **MFX
  (,6) pour les périphériques combinés** (détection :
  `IApoEndpointStore.GetDeviceProperty`, nouveau membre, lecture de
  \Properties) ; la sauvegarde mémorise le slot (`SlotUsed`, null = EFX
  pour les sauvegardes ≤ v3.9.7), capture l'origine MFX (CLSID + modes
  ,6), nettoie notre CLSID de l'AUTRE position (migration propre
  v3.9.7→v3.9.8), traite un CLSID déjà à nous comme ABSENT (comportement
  Equalizer APO sur ses propres GUID), répare dans la position
  mémorisée ; (ag) **auto-test COM pré-vol** (`ApoDllSelfTest` + rapport
  `ApoSelfTest` + décodage `ApoHresult`) — dans le processus élevé,
  AVANT toute écriture registre, la DLL déroule le parcours exact
  d'audiodg : LoadLibraryExW (code Win32 exact 5/126/193/1114/14001
  décodé), DllGetClassObject, CoCreateInstance VIA l'inscription COM
  réelle (avec différentiel fabrique directe : « inscription en cause »
  vs « DLL en cause »), QueryInterface des QUATRE interfaces (IIDs
  verrouillées par tests réflexion = miroir apo_abi.h),
  GetRegistrationProperties (CLSID + drapeaux relus), Initialize,
  LockForProcess au format de mixage 48 kHz/2 voies/float32
  (IAudioMediaType synthétique en CCW managé), APOProcess sur 256 trames
  d'un signal témoin (l'énergie doit traverser — courbe vide =
  transparence), UnlockForProcess ; un échec BLOQUE l'activation («
  ACTIVATION BLOQUÉE AVANT TOUTE MODIFICATION » + étape exacte + HRESULT)
  : la sortie ne peut plus être exposée à une DLL non instanciable ; (ah)
  **verdict différentiel complet** (`ApoFailureDiagnostics`, PUR) — le
  message « LE SON A ÉTÉ PRÉSERVÉ » embarque : auto-test (OK = DLL
  saine → cause côté moteur), prérequis moteur (signature / inscription
  moteur / inscription COM), trace de chargement + dernières lignes du
  journal (principal ou repli), ERREUR EXACTE de la sonde WASAPI
  (l'exception n'est plus avalée — HRESULT AUDCLNT_E_* décodé), position
  choisie, inventaire des positions (multi* signalés « Windows ignore
  alors les CLSID simples »), version de Windows ; même pré-vol et
  éléments en réparation ; le redémarrage du service audio attend
  désormais VRAIMENT l'état RUNNING (sc query, code numérique
  indépendant de la langue) avant de sonder.
- **As-built v1.8** : retour terrain v3.9.6 — « dès que l'EQ est activé sur
  une sortie, je n'ai plus de son sur cette sortie jusqu'à ce que je
  désactive l'EQ ». FAIT ÉTABLI (terrain documenté, cas Dolby/Win10) : un
  endpoint dont le moteur audio n'a pas pu INSTANCIER l'APO ne construit
  PAS son graphe — la sortie est MUETTE (« Failed to play test tone »,
  mixeur vide) ; l'APO refusée n'est pas contournée, elle TUE la sortie.
  Avant v3.9.6, le refus de signature (mode protégé) avait lieu AVANT la
  construction → son préservé ; depuis que la recette complète fait TENTER
  le chargement, tout échec d'instanciation coupe le son. Correctifs :
  (aa) **sonde WASAPI + restauration automatique** — après chaque
  activation/réparation, l'endpoint est sondé par un rendu partagé de ~120 ms
  de silence (témoin : une autre sortie vivante ⇒ verdict immédiat ; tout en
  cours de redémarrage ⇒ jusqu'à 4 tentatives) ; sortie morte ⇒ Disable
  (l'origine sauvegardée est restaurée) + redémarrage du service + verdict
  « LE SON A ÉTÉ PRÉSERVÉ : … restaurée automatiquement » (politique PURE
  `ApoActivationPolicy`, verrouillée par tests) ; (ab) **ACL bin lisible par
  TOUS** — « Tout le monde : lecture + exécution » comme Program Files
  (l'octroi précédent ne passait que par le compte NT SERVICE\Audiosrv,
  meilleur-effort et non vérifié : son échec éventuel privait audiodg de la
  lecture de la DLL → graphe mort ; icacls est désormais contrôlé par code
  de retour ET la DACL relue : `ApoAclVerifier.EveryoneCanReadAndExecute`) ;
  racine : + LOCAL SERVICE (S-1-5-19) Modifier par SID ; (ac) **l'écoute
  suit la sortie PAR DÉFAUT de Windows** (`PlaybackEngine.
  DefaultRenderOutputId` + `OutputFollowPolicy` pur : suivi tant que
  l'utilisateur n'a pas délibérément divergé — fin du « curseur branché sur
  les haut-parleurs pendant que le casque est le défaut ») ; (ad)
  **interface déclarée = IAudioProcessingObject** au registre
  (APOInterface0) ET dans GetRegistrationProperties natif — alignement sur
  les DEUX références (SysVAD ComponentizedApoSample.inx, CRegAPOProperties
  d'Equalizer APO) ; (ae) **journal de l'APO avec repli** — si
  %ProgramData%\RoomEq\apo\apo.log n'est pas inscriptible par audiodg,
  écriture dans %SystemRoot%\ServiceProfiles\LocalService\AppData\Local\
  Temp\RoomEqApo.log (l'emplacement du journal d'Equalizer APO, inscriptible
  par construction) ; le processus élevé lit les deux.
- **As-built v1.7** : retour terrain v3.9.5 — le panneau a nommé le maillon
  (« journal APO absent : la DLL n'a jamais été chargée par le moteur audio »)
  alors que l'activation, l'inscription COM et le CLSID EFX étaient corrects.
  FAIT ÉTABLI (recette complète d'Equalizer APO, doc développeur officielle
  + `DeviceAPOInfo.cpp`, croisée avec le sample SysVAD de Microsoft) : notre
  inscription était INCOMPLÈTE — (t) sans la DWORD **`DisableProtectedAudioDG=1`**
  sous `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Audio`, audiodg (mode
  audio protégé) REFUSE SILENCIEUSEMENT de charger toute DLL APO non signée
  — pas de journal, pas de message, la DLL n'est même jamais ouverte (notre
  DLL est non signée : compilée Zig) ; (u) la clé **`HKLM\SOFTWARE\Classes\
  AudioEngine\AudioProcessingObjects\{CLSID}`** doit exister avec des valeurs
  nommées (FriendlyName, Copyright, Major/MinorVersion, Flags, Min/Max
  Input/OutputConnections, MaxInstances, NumAPOInterfaces, APOInterface0) —
  c'est ce que fait `RegisterAPO` du DDK, exécuté par l'installateur
  d'Equalizer APO via regsvr32 ; (v) la valeur **MULTI_SZ de modes de
  traitement EFX** `{d3993a3f-99c2-4402-b5ec-a92a0367664b},7` doit contenir le
  mode par défaut `{C18E2F7E-933D-4965-B7D1-1EEF228D2AF3}` quand elle est
  absente (Equalizer APO l'écrit systématiquement à l'installation EFX).
  Correctifs : (w) `ApoEnginePolicy` (pur, testé Linux) détient les 12
  valeurs d'inscription — miroir EXACT de `GetRegistrationProperties` natif —
  et la sauvegarde/restauration de la valeur d'origine de la politique
  (`audio-policy.json`, capturée UNE fois, uniquement quand c'est NOUS qui
  changeons la politique : un Equalizer APO installé n'est jamais touché,
  ni modifié ni restauré) ; (x) `Deploy` (processus élevé) inscrit
  l'APO auprès du moteur, ouvre la politique si nécessaire — l'activation
  et la réparation deviennent AUTO-RÉPARATRICES ; (y) le registrar écrit le
  MULTI_SZ de modes EFX à l'activation (sauvegardé avant écriture, restitué
  à la désactivation, une sauvegarde d'avant-champ n'y touche jamais) et le
  rétablit à la réparation ; (z) `WindowsApoSystem.GetEngineStatus()` (lecture
  non élevée) alimente `ApoChainText` : un journal absent nomme désormais le
  prérequis manquant (« Windows refuse de charger la DLL (APO non signée) » /
  « pas inscrit auprès du moteur audio » / « inscription COM absente ») au
  lieu du générique « pilote ? ». Contrepartie honnête de la politique
  (affichée à l'activation et dans LISEZMOI) : les rares applications
  exigeant un chemin audio protégé (DRM) peuvent refuser de jouer — réversible
  à la désinstallation de l'EQ système, valeur d'origine restituée.
- **As-built v1.6** : retours terrain v3.9.4 — activation RÉUSSIE (« le
  routage fonctionne ») mais (a) « je n'entends pas de différence de son
  quand l'eq est activé (pourtant chargé) » et (b) volume gelé dans les deux
  sens après l'activation. Correctifs : (p) **volume** — le redémarrage
  d'audiosrv (`net stop/start`, inévitable à chaque activation/désactivation)
  invalide les objets COM WASAPI de l'application : `EndpointVolumeController`
  ne re-ciblait qu'au CHANGEMENT de sortie → lectures figées/écritures
  perdues pour toujours. `Reacquire()` re-énumère et re-bind le même
  périphérique ; appelé après chaque action EQ système (immédiat + à +1,5 s),
  à chaque rafraîchissement de périphériques, et en auto-guérison par le
  polling (au plus toutes les 2 s) ; (q) **chaîne observable** — le panneau
  affiche désormais chaque maillon de la courbe via `ApoChainText` (pur,
  testé Linux) : courbe envoyée (bandes actives/préampli/révision, relue
  d'active.json), écriture réussie ou refusée (`WindowsApoSystem.
  LastCurveWriteError`), et publication par le moteur audio (« nouvelle
  courbe publiee (rev N) » lu dans apo.log via `ReadApoLogTail`) — un EQ
  inaudible nomme son maillon : jamais écrite / écriture impossible /
  bypass / plate / APO qui ne publie pas / chaîne complète ✓ ; (r) **verrou
  ACL** — `ApoAclVerifier` vérifie par .NET (plus icacls au mieux-effort)
  que les Utilisateurs (S-1-5-32-545) ont Modifier sur le dossier partagé :
  l'activation ÉCHOUE avec un verdict explicite sinon (un icacls muet
  laissait l'application incapable d'écrire la courbe → EQ muet sans
  explication) ; (s) courbe initiale valide écrite à l'activation si
  active.json est absent (garantie d'existence + preuve d'inscriptibilité).
- **As-built v1.5** : retour terrain v3.9.3 — le DIAGNOSTIQUE par maillon
  (livré en v3.9.3) a nommé le coupable au premier essai : « impossible
  d'activer le privilège SeTakeOwnershipPrivilege (le point d'entrée
  CloseHandle est introuvable dans la DLL advapi32.dll) ». FAIT ÉTABLI :
  `CloseHandle` est un export de KERNEL32.DLL, il n'existe PAS dans
  advapi32.dll — importé d'advapi32 à tort depuis v3.9.2, son appel dans le
  `finally` de `EnablePrivilege` levait `EntryPointNotFoundException` qui
  REMPLAÇAIT le « return null » de succès (sémantique C# : une exception
  dans un finally écrase le résultat en cours de retour). Le privilège
  était donc réellement activé — le correctif v3.9.3 du LUID fonctionnait,
  `AdjustTokenPrivileges` réussissait — mais `EnablePrivilege` rapportait
  un échec, `TakeOwnership` abandonnait à sa première ligne et l'activation
  échouait en « clé protégée » alors que TOUT le mécanisme marchait.
  Correctifs : (n) `CloseHandle` importé de kernel32.dll ; (o)
  `ApoRegistrySecurityPinvokeTests` (2 tests de réflexion pure, exécutables
  sous Linux) verrouillent la DLL d'origine EXACTE des 6 imports natifs du
  takeover (OpenProcessToken/CloseHandle/GetCurrentProcess/
  LookupPrivilegeValue/AdjustTokenPrivileges/RegSetKeySecurity) — un
  binding cassé ne peut plus passer inaperçu jusqu'au terrain.
- **As-built v1.4** : retour terrain v3.9.2 — « TOUJOURS le problème de clé
  protégée » : le takeover était correct SAUF sa structure interop.
  `TOKEN_PRIVILEGES` déclarait le LUID comme `long` C# (alignement 8 octets)
  : le marshaling insérait 4 octets de padding après `PrivilegeCount` (24
  octets au lieu de 16) et le noyau lisait un LUID falsifié
  (`LowPart = 0` = le padding ; `Attributes = 0` = l'octet haut du long) →
  `AdjustTokenPrivileges` « réussissait » avec `ERROR_NOT_ALL_ASSIGNED` →
  `EnablePrivilege` concluait à tort « processus non élevé » et la prise de
  possession n'a JAMAIS été tentée (prouvé par marshaling réel : 24 octets
  `01-00-00-00-00-00-00-00-09-…` vs natif 16 octets `01-00-00-00-09-…`).
  Correctifs : (l) le LUID est désormais deux DWORD (`struct ApoLuid` +
  `ApoTokenPrivileges`, 16 octets exactement — verrouillé par
  `ApoRegistrySecurityLayoutTests`, exécutable sous Linux) ; (m) `EnablePrivilege`,
  `TakeOwnership`, `GrantAdminsFullControl` et `EnsureWritable` retournent un
  DIAGNOSTIC précis (code Win32, maillon défaillant) au lieu d'un booléen
  muet — un échec de takeover remonte désormais dans le verdict via
  `ApoRegistryRefusalException` (dérivée de `SecurityException`) : plus
  jamais d'« clé protégée » générique à débugger à l'aveugle.
- **As-built v1.3** : retour terrain v3.9.1 — l'activation était refusée par
  le registre MÊME ÉLEVÉE (« Requested registry access is not allowed ») :
  fait établi par le code source d'Equalizer APO (`helpers/RegistryHelper.cpp`
  — `takeOwnership` + `makeWritable` autour de la création de FxProperties),
  les clés d'endpoints MMDevices appartiennent à SYSTEM et leur DACL
  n'accorde PAS l'écriture aux administrateurs. Correctifs : (i) le store
  détecte le refus (`SecurityException`/`UnauthorizedAccessException`) et
  applique le mécanisme Equalizer APO via `ApoRegistrySecurity` :
  SeTakeOwnershipPrivilege → propriétaire = groupe Administrateurs
  (RegSetKeySecurity OWNER SEUL — le DACL en place n'est jamais modifié par
  cette étape), puis ACE FullControl héritée par les sous-clés (WRITE_DAC,
  droit implicite du propriétaire), puis RÉESSAI — idempotent et silencieux
  (false si le privilège manque : processus non élevé) ; (j) la SÉCURITÉ
  d'origine (owner + DACL, descripteur binaire Base64) est capturée AVANT
  la première écriture (`CaptureSecurity` : uniquement les clés où les
  admins n'ont PAS déjà FullControl — les autres ne changent pas), stockée
  dans la sauvegarde (`ApoEndpointBackup.Security`), et restituée à la
  désactivation APRÈS la restauration des valeurs (`RestoreSecurity`,
  SeRestorePrivilege pour réattribuer un propriétaire autre que soi —
  réversibilité STRICTEMENT supérieure à Equalizer APO, qui ne restitue
  jamais owner/DACL) ; (k) `SecurityException` résiduelle affiche un
  message français ACTIONNABLE (relancez + acceptez l'invite ; logiciel de
  sécurité) au lieu du « ex.Message » brut anglais.
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
   notre DLL, (b) l'inscription de l'APO auprès du moteur audio
   (`AudioEngine\AudioProcessingObjects\{CLSID}`, v3.9.6), (c) la politique
   `DisableProtectedAudioDG` — sauvegardée puis restituée à la désinstallation,
   jamais touchée si un tiers l'a déjà réglée — et (d) les valeurs
   FxProperties des endpoints explicitement choisis par l'utilisateur.
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
    inscription moteur audio, RESTAURE la politique audio d'origine (si nous
    l'avions modifiée) et supprime le dossier ProgramData — un utilisateur
    qui désinstalle retrouve Windows exactement comme avant (audit de
    registre à l'appui).
12. **Vérité produit** : les limites documentées (mode exclusif/ASIO hors
    périmètre, effets fabricant suspendus, DRM à confirmer au terrain) sont
    énoncées dans l'UI et la doc utilisateur, jamais cachées.

## Critères de test

- Registrar (faux registre — `ApoRegistrarTests`, 30 tests) : énumération ;
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
  sauvegarde héritée (sans le champ) ne supprime JAMAIS la clé** ;
  **v3.9.2 : capture de sécurité AVANT la première écriture (ordre vérifié
  par trace) et stockage dans la sauvegarde ; restitution APRÈS la
  suppression de la sous-clé (ordre vérifié par trace) ; aucune
  restitution si le snapshot est null (clé admins-inscriptibles ou
  sauvegarde héritée v3.7–v3.9.1) ; rétrocompatibilité disque d'un
  backups.json sans le champ Security ; inertie Linux étendue à
  CaptureSecurity/RestoreSecurity** ; **v3.9.6 : MULTI_SZ de modes EFX écrit
  à l'activation quand absent (mode par défaut, capture AVANT écriture) ;
  modes du pilote PRÉSERVÉS quand présents (comportement Equalizer APO) ;
  restitués à la désactivation (supprimés si absents à l'origine) ; une
  sauvegarde d'avant-champ ne les touche JAMAIS (compatibilité v3.7–v3.9.5) ;
  la réparation les rétablit après effacement simulé**.
- Politique/inscription moteur (`ApoEnginePolicyTests`, 9 tests — v3.9.6) :
  les 12 valeurs d'inscription verrouillées une à une (noms, types chaîne/
  DWORD conformes au sample SysVAD, contenu miroir EXACT du natif :
  FriendlyName/Copyright/versions 1.0/Flags=15/connexions 1-1-1-1/
  MaxInstances 64/APOInterface0 = IID_IAudioProcessingObjectRT) ; la
  politique n'autorise la DLL QUE pour la valeur exactement 1 (absente/0/2 =
  refus) ; round-trip JSON de la sauvegarde (origine absente = null,
  présente = entier, consommée = fichier supprimé, illisible = agir comme
  absente) ; constantes de la recette verrouillées (EFX `,7`, modes
  `{d3993a3f…},7`, mode par défaut, clé de politique, nom de valeur).
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
- ACL des dossiers partagés (`ApoAclArgsTests`, 3 tests — v3.9.7) : bin =
  Tout le monde (S-1-1-0) lecture + exécution, SYSTEM/Admins FullControl,
  ACL protégée, AUCUN droit d'écriture non-admin ; racine = Utilisateurs +
  NT SERVICE\Audiosrv + LOCAL SERVICE (S-1-5-19) Modifier ; les SID codés
  en dur verrouillés anti-typo.
- Politique d'activation (`ApoActivationPolicyTests`, 8 tests — v3.9.7) :
  croisement sonde×journal (saine / vivante-sans-journal / morte —
  uniquement depuis DeadGraph : une sortie DÉBRANCHÉE ne déclenche jamais
  de restauration) ; budget de re-tentes et arrêt immédiat quand un témoin
  vit.
- Suivi du défaut Windows (`OutputFollowPolicyTests`, 5 tests — v3.9.7,
  App) : divergence uniquement sur choix non-défaut délibéré ; suivi du
  nouveau défaut tant que pas divergé ; pas de suicide quand déjà conforme
  ou défaut inconnu ; rien de sélectionné + défaut connu = le prendre.
- Non-régression : suites xUnit 100 % vertes (550/550 : Core 237, Audio
  233, App 80) et harnais VST/AU/APO inchangés (APO : l'interface déclarée
  des propriétés d'enregistrement verrouillée à IAudioProcessingObject).
