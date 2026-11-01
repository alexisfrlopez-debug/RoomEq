# REPRISE — RoomEq v3 (recodage Triade)

> **À lire en premier au début de chaque session.** Ce fichier est le point
> d'entrée : état, restauration, règles, prochaine étape.

## 0. Résumé express

| | |
|---|---|
| **Projet** | RoomEq **v3.8.0** — recodage complet TRIADE **cristallisé et publié**, + export FabFilter Pro-Q 3 (D17), + anti-boucle au routage (D18), + greffon AudioUnit Ableton (D19), + installation « 1 clic » (D20 : Setup.exe Windows Velopack + .pkg macOS), + greffon VST 2 32 bits Windows (D21) avec correctifs v3.4.x/v3.5.x, v3.6.0 : l'AU en parité fonctionnelle du VST (D23), **v3.7.0 : EQ SYSTÈME SANS PILOTE (D24, M-AUD-09)** ; et **v3.8.0 : CONSOLE ALLÉGÉE (D25) — les tranches « entrée/sortie » (captures/boucles Windows) et le routage par carte virtuelle sont retirés : l'onglet Mixage met l'EQ système en tête, la console ne garde que ses sources internes (lecteur, générateur, pont DAW ASIO), le bus master contourne son EQ quand l'APO couvre la sortie (anti double-correction), migration automatique des défauts Windows de l'ancien routage + suppression du pilote hérité ; macOS inchangé** — 502 tests xUnit + harnais APO 112 vérifications |
| **Base fonctionnelle** | v2.2.2 (GitHub `alexisfrlopez-debug/RoomEq` @ `deff25c`) |
| **Ce dépôt** | `/home/z/my-project/workspace/RoomEq` (sandbox recyclée en session 11 : re-cloné depuis GitHub, branche `v3-recodage`) — miroirs Drive + GitHub `alexisfrlopez-debug/RoomEq` |
| **Référence legacy** | clone git `/home/z/my-project/roomeq-git-ref` + miroir Drive `/home/z/my-project/roomeq-legacy` |
| **SDK** | `export PATH="$HOME/.dotnet:$PATH"` (8.0.427 ; réinstaller : `curl -sSL https://dot.net/v1/dotnet-install.sh \| bash -s -- --channel 8.0 --install-dir "$HOME/.dotnet"`) |
| **Vérifier** | `cd src && dotnet build RoomEq.sln -c Debug` puis les 3 suites (`dotnet test ../tests/RoomEq.*.Tests`) — 502 verts ; APO : `natif/roomeq-apo/build-tests.sh` (**112 vérifications**) puis `natif/roomeq-apo/build-apo.sh` (DLL x64+arm64 ; Zig dans `~/tools/zig-*` ou `/home/z/my-project/tools/zig-*`) ; greffon AU : `cmake -S natif/roomeq-au -B build-au && cmake --build build-au && ./build-au/roomeq_au_tests natif/roomeq-au/tests/data` (**15 671** vérifications : parité + écriture JSON + banque + éditeur + auto-chargement ; gcc 14 : faux positif maybe-uninitialized de biquad.cpp neutralisé — plus de -Werror sur le cœur, aligné sur le VST) ; greffon VST : `cmake -S natif/roomeq-vst -B build-vst && cmake --build build-vst && ./build-vst/roomeq_vst_tests natif/roomeq-au/tests/data && ./build-vst/roomeq_vst_host build-vst/RoomEqVst.so natif/roomeq-au/tests/data` (430 + 41) puis `natif/roomeq-vst/build-vst.sh` (DLL win32 ; Zig dans `~/tools/zig-*` ; si la bibliothèque casques change : `python3 scripts/gen_headphone_bank.py` d'abord — sort désormais dans le cœur AU) ; macOS : `bash natif/roomeq-au/build-macos.sh` (build + auval) |
| **Sauvegarde** | `scripts/sync_backup.sh` — tests + commit + push GitHub + miroir Drive (**à chaque lot de travail**, D7) ; les **livrables** (zips `RoomEq-*` de `download/`) sont téléversés dans `Equalizer/RoomEq-v3/Livrables` par le même miroir (resumable, idempotent MD5) ; **RÈGLE DE LIVRAISON D22 (permanente)** : toute livraison = synchro Git+Drive obligatoire + archivage automatique des anciennes versions dans `Livrables/Archive` (voir § 2bis) |
| **Jetons** | `../triade/.gdrive/` (GitHub + Drive OAuth) — jamais commités |

## 1. État (fin session 21 — v3.8.0 : console allégée D25, tranches entrée/sortie retirées)

**Session 21 (2026-11-02)** : « Vu les dernières modifications (EQ dans le
moteur Windows), les tranches de mixage "entrée" et "sortie" n'ont plus
aucun intérêt (et peuvent causer des conflits ou confusion). »

- **D25 ACTÉE** : depuis l'EQ système APO (D24), les tranches liées aux
  périphériques Windows — « entrée » (captures : micros, câble virtuel) et
  « sortie » (boucles WASAPI) — doublaient la correction, déclenchaient
  l'anti-boucle et désorientaient. Elles sont retirées de l'application,
  avec le routage qui les alimentait.
  - **Console** : `AvailableSources` = DAW (ASIO) + Lecteur + Générateur
    (+ périphérique virtuel macOS) ; `AddTrack` refuse les sources
    périphériques (défense en profondeur, `MixerTrackPolicy.cs` pur et
    testé) ; tranches par défaut = Lecteur + Générateur ; le badge de
    tranche devient toujours « INTERNE ».
  - **Onglet Mixage réorganisé** : panneau « EQ système » EN TÊTE, barre de
    console simplifiée (« Démarrer la console » / « ■ Arrêter la console »,
    Auto au lancement, Preset auto par sortie, ⟳, Réglages son, état ASIO) ;
    supprimés : sélecteur de protocole, installation/suppression/
    « Définir par défaut », cases « Router dès l'installation » et
    « Entrée par défaut → carte virtuelle », guide carte virtuelle Windows.
  - **Anti double-correction (le conflit réel)** : `ApoOnMaster` — si l'EQ
    système est actif sur la sortie master, le bus master CONTOURNE son
    propre EQ (la courbe est déjà appliquée par le moteur Windows) ;
    recalculé au changement de sortie et à chaque état APO ; la tranche
    MASTER affiche qui applique l'EQ.
  - **Migration pilote hérité** : si « RoomEq Input » est encore la sortie
    par défaut de Windows (état v3.6/v3.7), la sortie d'origine mémorisée
    dans mixer.json est restaurée automatiquement (une fois par session,
    `HandleLegacyVirtualDefault`), sinon message actionnable ; bouton
    « Supprimer le pilote » proposé tant qu'une carte héritée est détectée.
  - **Persistance purgée** : mixer.json ne restaure plus les tranches
    capture/loopback (kinds ignorés) ; champs obsolètes lus pour tolérance
    puis non réécrits.
  - **macOS inchangé** : routage « RoomEq Virtual » + guide dédiés.
  - Couche audio M-AUD-03 INTACTE (capacités testées = défense en
    profondeur) ; tests Audio 168 inchangés.
- **Tests** : 502/502 xUnit (Core 237, Audio 168, App 97 dont
  `MixerTrackPolicyTests` +18) ; natifs inchangés.
- **Prochaine étape** : validation terrain Windows (migration depuis une
  v3.7 avec pilote + routage actif : sortie par défaut restaurée ? pilote
  supprimable ? EQ système + console DAW sans double correction ?).

## 1bis. État (fin session 20 — v3.7.0 : EQ système APO livré + correctifs gel/désynchro)

**Session 20 (2026-11-02)** : « Je veux la solution driver pour avoir une
carte son RoomEq dans les périphériques Windows et une appli auto-suffisante
et user friendly ; corriger le re-sondage des périphériques à chaque clic
(ça freeze) — plutôt un bouton refresh ; et le son se désynchronise de
l'image en vidéo. »

- **D24 ACTÉE et livrée** (vague 9 complète) : la « carte virtuelle RoomEq »
  littérale est impossible gratuitement (signature noyau) — l'EQ système
  APO livre le MÊME résultat utilisateur avec un flux plus simple :
  installer l'appli → onglet Mixage → panneau « EQ système (sans pilote) » →
  « Activer l'EQ sur cette sortie » → tout le son de cette sortie traverse
  l'égaliseur (latence nulle). Le pilotage reste l'onglet Égaliseur, en
  temps réel.
  - **Natif** `natif/roomeq-apo/` (N-APO-01) : ABI COM APO auto-contenue
    (vérifiée SDK 10.0.10240 + Equalizer APO), sAPO position EFX, cœur DSP
    D23 réutilisé, thread de fond + swap atomique, **fondu enchaîné 20 ms**
    entre courbes, horodatage fin (ns/100 ns — st_mtime raterait les
    réécritures pendant un glissement), cimetière de chaînes (le chemin RT
    ne libère jamais de mémoire), règle de survie (jamais d'échec : 10
    échecs = effets système désactivés). Harnais **112 vérifications, 0
    échec** (parité bit à bit, 0 alloc en régime établi, continuité à la
    réécriture, fondu reproduit au demi-ULP, 6 canaux/mono/reformatage…).
    DLL x64 452 Ko + arm64 412 Ko (Zig, PE32+ contrôlés par
    `scripts/check_pe_exports.py`).
  - **C#** `src/RoomEq.Audio/SystemApo/` (M-AUD-09) : `ApoRegistrar` (logique
    pure, faux registre en CI — ordre sauvegarde→écriture, restauration
    exacte, idempotence, réparation, anti-boucle), `WindowsApoEndpointStore`
    (registre MMDevices, inerte hors Windows), `ApoConfigStore` (active.json
    atomique + révision), `ApoSetupRunner` (relance élevée
    `--apo-setup`, refus UAC = rien modifié), `ApoSetupCommands` (déploiement
    %ProgramData% + ACL par SID [bin en lecture seule standard], inscription
    COM, redémarrage audiosrv), `WindowsApoSystem` (façade UI + bypass
    mesure câblé sur `RoomMeasurementEngine.SystemEqBypass`).
  - **UI** : panneau « EQ système » dans l'onglet Mixage (état par sortie,
    activer/désactiver/réparer/tout retirer, aide avec limites honnêtes),
    courbe poussée via l'anti-rebond existant, réparation au branchement à
    chaud.
- **Correctif gel** : `RefreshDevices` refait en **une seule passe COM en
  arrière-plan** (`AudioDeviceCatalog.CaptureSnapshot` + sémaphore, plus
  jamais 6 balayages + ASIO sur le thread UI) ; bouton ⟳ lié à la commande
  asynchrone animée ; hors Windows : synchrone inchangé (CI déterministe).
- **Correctif désynchro A/V** : latence console 50 → 30 ms + l'EQ système
  APO (latence nulle) comme voie « tout le son » recommandée.
- **Validation** : 484/484 xUnit (Core 237, Audio 168 [+23 APO], App 79) +
  harnais APO 112 + VST/AU inchangés. Version 3.7.0, packaging APO intégré
  (`package_v3.sh` + `build_setup.sh` déploient `apo/RoomEqApo_{x64,arm64}.dll`).
- **Terrain restant** : checklist Windows réelle (Realtek/Intel/NVIDIA/BT/
  HDMI, MAJ pilote, mode exclusif) — le comportement APO est éprouvé par
  Equalizer APO à grande échelle mais NOTRE binaire doit être validé sur
  machine cible.

## 1bis. État (fin session 19 — étude : EQ système sans pilote, recommandation APO)

**Session 19 (2026-11-01)** : « Revenir à la version installable Windows :
l'application doit installer son propre driver de carte son virtuelle pour ne
plus dépendre de VB-Cable, mais le "driver signé Microsoft" est payant —
trouver un contournement user friendly, sans mode test. Pourquoi ce problème
alors qu'il existe des drivers non signés installables ? »

- **Constat** : l'appli a déjà son pilote (`roomeqvad.sys` scellé v2.2.2) —
  le mur est sa **signature test** (mode test + Secure Boot off, refusé par
  défaut par I-INS-05). VB-CABLE est le palliatif installé en pratique.
- **Démêlage du paradoxe** : les « drivers non signés » qui s'installent sont
  soit signés Microsoft sans que ça se voie (c'est le cas de VB-CABLE), soit
  du user-mode (ASIO, APO), soit des class drivers, soit sur des machines
  Secure Boot off / en mode test. Aucune voie gratuite légale pour signer
  un driver noyau (EV 226–669 $/an + Hardware Dev Center).
- **Recommandation : APO** — Audio Processing Object user-mode chargé par
  `audiodg.exe` (position EFX, FxProperties par endpoint) : zéro pilote,
  zéro signature Microsoft, zéro mode test, zéro dépendance tierce,
  installation = 1 UAC réversible. Preuve à l'échelle : Equalizer APO
  (non signé, des millions d'installations Win 10/11). Architecture
  renversée : l'EQ vit dans le moteur audio Windows, l'appli devient
  télécommande de la courbe (%ProgramData%, format preset D23, cœur DSP
  partagé VST/AU réutilisé). Limites honnêtes : exclusif/ASIO/KS
  contournent, par périphérique, effets fabricant suspendus (sauvegardés),
  auto-réparation après MAJ pilote, ARM64 optionnel, DRM à confirmer.
- **Livrables de la session** : `docs/EQ-SYSTEME-APO.md` (analyse sourcée,
  4 options comparées, architecture, plan vague 9 en 4 lots) + spec
  **I-AUD-09** `I/audio/I-apo-systeme.md` (M-AUD-09/N-APO-01 : ApoRegistrar
  + ApoConfigStore + RoomEqApo.dll, 12 invariants, critères de test) +
  CARTE (module prospectif ⬜ spec prête).
- **Décision D24 PROPOSÉE (non consignée)** : l'EQ système Windows passe par
  l'APO ; pilote noyau = option avancée ; signature EV = voie complémentaire
  long terme. **En attente de validation utilisateur** → puis vague 9
  (lot 1 : `natif/roomeq-apo/` + harnais `roomeq_apo_host`).
- Aucun code modifié : tests xUnit 461/461 inchangés, natifs non touchés.

## 1bis. État (fin session 18 — v3.6.0 : l'AU en parité fonctionnelle du VST)

**Session 18 (2026-11-01)** : « parfait, tout fonctionne [VST 3.5.2].
Maintenant il me faut la version pour Ableton sur macOS en AU » — livrer
l'AU **avec la même expérience que le VST validé** (édition directe,
banque casques, VU-mètres), décision **D23**.

- **Cœur partagé élargi (D23)** : `headphone_bank.*` + `headphone_bank_data.h`
  et `preset_write.*` déplacés de `natif/roomeq-vst/vst/` vers le cœur
  `natif/roomeq-au/core/roomeq/{headphones,preset}/` (ports de
  RoomEq.Core : HeadphonePresetLibrary, sérialisation EqPreset) — namespaces
  `roomeq::bank` / `roomeq::preset::ToJson` ; le VST les consomme depuis le
  cœur (includes + CMake ajustés, **zéro changement de comportement** :
  430 + 41 verts) ; `gen_headphone_bank.py` régénère au nouvel emplacement.
- **Couche d'édition portable** `natif/roomeq-au/edit/preset_editor.{h,cpp}`
  (`roomeq::edit::PresetEditor`) : état de preset éditable thread-safe
  (json/ffp, clamps des bornes de l'application, re-sérialisation JSON à
  chaque mutation, banque, auto-chargement par récence avec saut des
  presets plats) — **testée sous Linux**, l'intégration AU reste mince.
- **AU v3.6.0** : nouvelles propriétés custom bornées (`'RPbd'` instantané
  binaire des bandes, `'RPeb'`/`'RPab'`/`'RPbo'` édition, `'RPlh'` banque,
  `'RPlv'` VU) ; vue Cocoa réécrite en miroir de l'éditeur VST 560×320
  (glisser/molette/double-clic/menu contextuel, listes casques, VU 30 Hz,
  bannières orange, version) ; auto-chargement du magasin
  `~/.config/RoomEq/presets` à la construction (l'état du projet reste
  maître) ; journal `~/Library/Logs/RoomEq/roomeq-au.log` (open /
  auto-chargement / initialisation / premier bloc / close avec blocs
  traités) ; VU + compteur alimentés par le noyau du canal 0 (atomiques).
- **Validation** : AU **15 671 vérifications** natives (+ écriture JSON
  aller-retour exact, banque 321 modèles, éditeur, auto-chargement par
  récence avec mtimes nanosecondes) ; VST 430 + 41 non régressé ; xUnit
  461/461. Info.plist 3.6.0/30600, CMake AU 3.6.0, props + package_v3.sh
  alignés. Spec I-AUD-07 passée en **v2**.
- **Livrable** : `RoomEq-AU-3.6.0-macos.zip` + `.pkg` construits par la CI
  macOS (build + signature ad hoc + `auval -v aufx Rmeq Rmeq`), joints à la
  release du tag `v3.6.0`, puis miroir Drive + archivage D22 (l'AU 3.3.0
  part automatiquement dans `Livrables/Archive`).
- **Test utilisateur décisif** : installer le .pkg, insérer « RoomEq:
  Correction » dans Live (AU activés dans les préférences) — le preset le
  plus récent de l'application se charge tout seul ; sinon choisir un
  casque dans les listes ; les VU In/Out doivent bouger à la lecture et le
  journal doit montrer « process : … preset « … » … ».

## 1bis. État (fin session 17 — v3.5.2 : le DAW appelle enfin le traitement)

**Session 17 (2026-11-01)** : cinquième retour VST — « les VU-mètres In/Out
n'affichent rien quand le VST est actif, toujours pas de modification du
son » + journal fourni : **« close : 0 blocs traités »**, aucune ligne
« premier bloc ».

- **Diagnostic (structurel)** : le journal prouve que l'hôte n'appelle
  JAMAIS `processReplacing`. Cause racine vérifiée contre le SDK Steinberg
  2.4 authentique (miroir public GitHub) + rust-vst : **cinq rangs de la
  structure `AEffect` manquaient** dans notre ABI autodéclarée entre
  `initialDelay` et `uniqueID` (dépréciés VST 1.x `realQualities`,
  `offQualities`, `ioRatio` + pointeurs libres `object`, `user`) →
  `processReplacing` était écrit 20 octets trop tôt (offset 60 au lieu de
  80 en 32 bits) → l'hôte lisait NULL dans `future[]` et passait l'audio
  tel quel. **Bug présent depuis la v3.4.0** ; invisible pour le mini-hôte
  (même structure des deux côtés — auto-cohérence, 38/38 verts).
- **Preuve binaire** : le constructeur de la 3.5.1 stocke le pointeur à
  `[ecx+0x3c]` (60), celui de la 3.5.2 à `[ecx+0x50]` (80 = offset ABI).
- **Correctif v3.5.2** : rangs comblés (à 0/nullptr, inutilisés mais
  comptant dans l'alignement) + **verrou triple** : static_asserts
  d'offsets dans `vst2_abi.h` (évalués par le build Zig 32 bits ET le
  build de test 64 bits), test « vue hôte externe » dans la suite native
  (lecture des champs PAR OFFSETS EN DUR du SDK, puis APPEL du pointeur
  lu : 1 bloc traité, VU alimenté — ce test aurait attrapé le bug),
  contrôle équivalent au mini-hôte. Journal enrichi : taux, taille de
  bloc, mains (resume/suspend).
- **Validation** : 430 vérifications natives (+21) + 41 mini-hôte (+3),
  parité 0.00e+00, DLL win32 reconstruite (PE32 i386, exports OK).
  Aucun changement DSP — correctif purement déclaratif.
- **Livrable** : `RoomEq-VST-3.5.2-win32.zip` — remplacer la DLL, la
  version affichée en bas à droite doit lire **3.5.2**, rescanner, puis
  lancer la lecture : les VU doivent bouger et la ligne « process : … »
  apparaître dans le journal.

## 1bis. État (fin session 16 — D22 : règle de livraison + archivage Drive)

**Session 16 (2026-11-01)** : demande « ajoute une règle permanente dans le
fichier d'instructions du projet pour faire systématiquement une synchro git
et drive à chaque livraison, + archivage des anciennes versions pour éviter
toute confusion ».

- **D22 — règle permanente de livraison** (§ 2bis de ce fichier) : tests
  (R5) → commit + push GitHub → miroir Drive → archivage — **une version non
  synchronée n'est pas livrée**. S'applique à toute livraison (zip appli,
  greffon VST/AU, installateur).
- **Archivage automatique** implémenté dans `scripts/drive_sync_roomeq.py`
  (`archive_old_versions`) : familles de produits (appli `RoomEq-<v>-*`,
  `RoomEq-VST-<v>-*`, `RoomEq-AU-<v>-*`) + tri de versions
  (3.0.0-dev.1 < 3.0.0 < 3.4.0) ; au premier niveau de `Livrables` ne reste
  que la **dernière version de chaque produit**, les versions antérieures
  sont **déplacées** (jamais supprimées) dans `Livrables/Archive` ; les
  fichiers non versionnés (`RoomEq-win-Setup.exe`, LISEZMOI) restent.
  Idempotent, exécuté à CHAQUE synchro Drive.
- **Premier archivage (entrée en vigueur immédiate)** : 24 anciens fichiers
  déplacés — le premier niveau de `Livrables` ne contient plus que
  `RoomEq-3.4.0-*` (appli, 3 fichiers), `RoomEq-AU-3.3.0-macos.pkg`,
  `RoomEq-VST-3.5.1-win32.zip`, `RoomEq-win-Setup.exe` et les deux LISEZMOI.
- Aucun changement applicatif (`src/`, `natif/` intacts) ; validation R5 :
  461/461 tests xUnit verts, puis commit + push GitHub + miroir Drive
  exécutés selon la nouvelle règle (la règle s'applique à elle-même).

## 1bis. État (fin session 15 — v3.5.1 : fenêtre du greffon VST entière)

**Session 15 (2026-11-01)** : quatrième retour VST — « l'interface n'est pas
visible entièrement, certaines parties ne sont pas accessibles ».

- **Cause (une ligne)** : le rect déclaré à l'hôte (`kEffEditGetRect`)
  était resté à **520×240** (taille d'avant la rangée casques) alors que
  la fenêtre de l'éditeur fait **560×320** depuis la v3.5.0 — le DAW
  dimensionne sa fenêtre sur le rect déclaré : la rangée des contrôles
  (« Charger… », listes casque, « Appliquer ») et le VU-mètre étaient
  coupés. Preuve binaire : octets `{0,0,240,520}` dans la DLL 3.5.0,
  `{0,0,320,560}` dans la 3.5.1.
- **Correctif structurel (v3.5.1)** : `vst/editor_geometry.h` = source de
  vérité unique (fenêtre créée ET rect déclaré dérivent des mêmes
  constantes), static_asserts de cadrage des contrôles dans
  EditorWin32.cpp, test dédié dans la suite native — une dérive future
  casse la compilation ou les tests. Aucun changement DSP/audio.
- **Validation** : 409 + 38 vérifications natives/ABI (parité 0.00e+00,
  +6 tests géométrie), versions alignées (kVersion 3501, CMake,
  Directory.Build.props, package_v3.sh). Livrable :
  `RoomEq-VST-3.5.1-win32.zip` (remplacer la DLL, la version affichée
  en bas à droite doit lire **3.5.1**, puis rescanner).

## 1bis. État (fin session 14 — v3.5.0 : égaliseur éditable + banque casques + diagnostic)

**Session 14 (2026-11-01)** : troisième retour VST (« toujours aucune
modification du son — vérifie le routage ») + deux demandes (égaliseur
modifiable dans le greffon, banque écouteurs/casques en liste déroulante).

- **Routage audité et couvert** : ProcessChannels gère correctement in-place,
  out-of-place, accumulation et suspension/reprise — trois nouveaux tests
  (parité 0.00e+00). Aucun défaut de câblage côté greffon.
- **Édition directe (v3.5.0)** : drag d'un point (fréquence + gain), molette
  = Q, double-clic courbe = nouvelle bande, double-clic point = activer/
  désactiver, clic droit = menu type/supprimer. API thread-safe dans le
  greffon, chunk re-sérialisé en JSON au format exact de l'application
  (preset_write.cpp — le cœur AU reste scellé), bornes identiques à l'app.
- **Banque casques embarquée (v3.5.0)** : 321 modèles AutoEq dans la DLL
  (blob binaire « RHP1 » 57,5 Ko généré par scripts/gen_headphone_bank.py,
  décodage défensif, conversion miroir de ToEqPreset) ; deux listes
  déroulantes marque/modèle + bouton « Appliquer » dans l'éditeur.
- **Diagnostic intégré** : VU-mètre In/Out, version affichée en bas à
  droite, compteur de blocs au journal de fermeture.
- **Validation** : 403 + 38 vérifications (parité 0.00e+00, +106 tests),
  461/461 xUnit. Livrable : RoomEq-VST-3.5.0-win32.zip (DLL 588 Ko).

## 1bis. État (fin session 13 — v3.4.2 : correctif « preset plat » + synchro Drive/Git)

**Session 12 (2026-11-01)** : second retour utilisateur du greffon VST —
« le VST affiche 10 bandes de fréquence mais sur la courbe je ne vois pas
les 10 bandes ». Correctif **v3.4.2** (détail dans WORKLOG.md session 12) :
l'auto-chargement **saute les presets plats** (toutes bandes à 0 dB sans
préampli — l'état `EqPreset.Flat()` de l'onglet Égaliseur sauvegardé tel
quel) et prend le premier preset qui corrige ; l'éditeur marque chaque
bande d'un **point sur la courbe** et signale les presets plats ; 297 + 38
vérifications natives/ABI vertes (parité 0.00e+00). Livrable :
`RoomEq-VST-3.4.2-win32.zip`.

**Session 13 (2026-11-01)** : demande « fais une synchro drive et git » →
ce fichier.

- **Jetons restaurés** : `../triade/.gdrive/` (GitHub `token_git.txt`,
  Drive `oauth_token_relais.json` + `client_secret.json`) récupérés d'un
  reliquat `/tmp/my-project/triade/.gdrive/` de la session 11 (layout
  antérieur du dépôt), copiés avec permissions restrictives (700/600) et
  **vérifiés** : `git ls-remote` OK (branche distante `2f2839e`) ; refresh
  OAuth Drive OK (dossier « Equalizer » accessible).
- **Scripts re-pointés** : `scripts/drive_sync_roomeq.py` — chemins par
  défaut `ROOMEQ_REPO`/`ROOMEQ_TOKENS` alignés sur le layout actuel
  (`/home/z/my-project/workspace/RoomEq` et `.../workspace/triade/.gdrive`).
- **R5 verify-then-commit** : SDK .NET 8.0.427 réinstallé (sandbox
  recyclée), 461/461 tests verts (237 Core + 145 Audio + 79 App).
- **Synchro** : commit session 13 + push GitHub (branche `v3-recodage` :
  v3.4.1 `60d7d42` + v3.4.2 `5e7c929` + session 13 ; tags) + miroir Drive
  (dépôt, livrables, bundle git).

## 1bis. État (fin session 11 — v3.4.1 : correctif du greffon VST)

**Session 11 (2026-11-01)** : premier retour utilisateur du greffon VST —
« je viens de tester le vst, j'ai aucune différence de son quand il est
activé » (Live 9) :

- **Diagnostic** : comportement PAR CONCEPTION — le greffon est TRANSPARENT
  (identité bit à bit) tant qu'aucun preset n'est chargé ; l'état « Preset :
  Transparent » en petit gris dans l'éditeur passait inaperçu et rien
  n'indiquait qu'il fallait charger un preset exporté de l'application.
  Le cœur DSP est sain (parité bit à bit re-vérifiée sur la session).
- **Correctif v3.4.1** (M-AUD-08, spec I-AUD-08 **v2**) : **auto-chargement**
  à `effOpen` du preset **le plus récent** du magasin de l'application
  (`%APPDATA%\RoomEq\presets`, variable `ROOMEQ_PRESETS_DIR` en priorité),
  essais du plus récent au plus ancien jusqu'à acceptation (corrompu ne
  bloque pas les autres) ; **précédence du chunk** (l'état du projet DAW
  écrase l'auto-chargement ; chunk vide = transparent) ; **bannière orange
  « Aucun preset chargé »** dans l'éditeur Win32 ; **journal du premier bloc
  audio** (taux, preset, bandes actives, préampli, bypass) dans
  `%LOCALAPPDATA%\RoomEq\logs\roomeq-vst.log`.
- **Validation** : 264 vérifications natives (+36 : auto-chargement — plus
  récent, repli sur valide, précédence chunk, .ffp, répertoire absent,
  **parité C# après auto-chargement**) + 38 mini-hôte, parité 0.00e+00 ;
  DLL win32 reconstruite (Zig 0.14.1, SSE2) et contrôlée (PE32/exports,
  imports kernel32 FindFirstFileW).
- **Note environnement** : sandbox recyclée avant la session → dépôt
  re-cloné depuis GitHub (`workspace/RoomEq`, branche `v3-recodage`), Zig
  re-téléchargé dans `~/tools/`, CMake via pip ; qemu-i386 absent (les 36
  nouvelles vérifications n'ont pas été rejouées en 32 bits — documenté) ;
  jetons `../triade/.gdrive/` perdus (Drive/push à rétablir).

## 1ter. État (fin session 10 — v3.4.0 : greffon VST 2 32 bits Windows)

**Session 10 (2026-11-01)** : demande développeur « fais un VST 32 bits » →
décision **D21** :

- **D21 — greffon VST 2 32 bits Windows « RoomEq Correction »** (module natif
  M-AUD-08/N-VST-01, `natif/roomeq-vst/`, spec `I/audio/I-greffon-vst.md`) :
  applique les presets RoomEq `.json`/`.ffp` dans un DAW Windows 32 bits
  (Live ≤ 9.7, FL 11, Cubase 5, REAPER 32). Cœur C++ réutilisé tel quel du
  greffon AU (D19 — parité bit à bit) ; ABI VST 2.4 autodéclarée
  (`vst/vst2_abi.h` : aucune copie d'en-tête Steinberg, constantes vérifiées
  contre le SDK authentique + vestige) ; paramètre « Preamp » automatisable,
  chunks, bypass `effSetBypass`, éditeur Win32 (Charger…/glisser-déposer/
  courbe), exports `VSTPluginMain`+`main` (alias `.def`). Cross-compilation
  **Zig 0.14.1** Linux → win32 (SSE2 forcé) → `RoomEq.dll` PE32 i386 dans
  `RoomEq-VST-<v>-win32.zip` (CI `plugin-vst`). Validation : 228 + 38
  vérifications natives/ABI (parité **bit à bit**), **228 en exécution 32
  bits (qemu-i386)**, contrôles PE32/exports. Limites documentées : écart ulp
  CRT Windows ≤ ~1e-13 (inaudible), non testé dans un hôte Windows réel
  (journal `%LOCALAPPDATA%\RoomEq\logs\roomeq-vst.log`), stéréo 2/2.
  Outils persistés : `natif/roomeq-vst/build-vst.sh` ; Zig + CMake portables
  dans `~/tools/` (aucun sudo).

## 1bis. État (fin session 9 — v3.3.0 : installation « 1 clic »)

**Session 9 (2026-11-01)** : demande développeur « une installation en
1 clic plutôt qu'en ligne de commande » → décision **D20** :

- **D20 — installation « 1 clic »** (empaquetage pur, aucun fichier de
  `src/` modifié, Carte inchangée à 44 modules 🔒) : **Windows** →
  `RoomEq-win-Setup.exe` (Velopack 1.2.161 épinglée, **cross-compilé
  Linux → win-x64** via `vpk "[win]" pack`, `scripts/build_setup.sh`) :
  double-clic → installé (raccourcis Bureau + Menu démarrer, désinstalleur,
  installation utilisateur sans admin dans `%LOCALAPPDATA%\RoomEq`,
  variante publique — RoomEq-test.exe/telemetrie.json exclus) ; **macOS** →
  `RoomEq-AU-<v>-macos.pkg` (CI `plugin-au`, `pkgbuild`, postinstall
  xattr + re-scan registrar) pour le greffon Ableton. CI : nouveau job
  `setup` qui joint le Setup.exe aux releases. `--skipVeloAppCheck` :
  aucune dépendance tierce ajoutée à l'application ; mises à jour
  automatiques = décision future (nupkg déjà produit en artefact CI).
  Distinct de la vague 8 (D11 : recodage de l'installateur natif du
  pilote, non lancé).

**Session 8 (2026-11-01)** : deux demandes développeur → décisions **D18**
et **D19** :

- **D18 — anti-boucle au moment du routage** (M-AUD-03 spec **v2**) :
  `SetOutput`/`AddChannel` refusent immédiatement tout croisement
  entrée/sortie (piste loopback ↔ périphérique master, câble virtuel,
  périphérique RoomEq macOS), AVANT toute mutation d'état ; l'UI grise les
  sorties en conflit (`OutputDeviceInfo.BlockedReason`), n'en sélectionne
  jamais une automatiquement, et `Start` revalide (défaut résolu compris) ;
  suppression d'une piste → sortie débloquée.
- **D19 — greffon AudioUnit « RoomEq Correction » `.component`** (module
  natif **M-AUD-07** / **N-AU-01**, `natif/roomeq-au/`, spec
  `I/audio/I-greffon-au.md`) : applique les presets RoomEq (.json
  application ou .ffp Pro-Q 3) dans Ableton Live (macOS) ; cœur C++ port
  EXACT de RoomEq.Core — **parité bit à bit** (118 vérifications natives
  sur références générées par `tools/gen_reference`, écart nul) ;
  AudioUnitSDK Apple 1.4.0 vendangé (Apache-2.0, D19 écarte JUCE/AGPL) ;
  vue Cocoa (charger/glisser/courbe), préampli automatisable, état de
  session ; CI macOS `plugin-au` (build + signature ad hoc + **auval**) →
  `RoomEq-AU-<version>-macos.zip` dans les releases.

Version **3.2.1** (3.2.0 = premier jet, CI macOS rouge sur les includes
libc++ — `std::max` sans `<algorithm>` ; correctif conforme au protocole
« patchs à contrat stable », tag 3.2.0 conservé immuable), 44 modules 🔒,
**461/461 tests** (237 Core + 145 Audio + 79 App) + 118 vérifications
natives ; greffon compilé universel (arm64 + x86_64).

**Session 7 (2026-11-01)** : demande développeur « exporter un preset
d'égalisation en .ffp pour FabFilter Pro-Q 3 » → décision **D17** : nouveau
module **M-IO-06** (`FabFilterProQ3.cs`, spec `I/io/I-export-fabfilter.md`,
format binaire FQ3p établi par triple recoupement et validé par relecture
croisée avec le toolkit Python de référence) + M-APP-04 étendu (partial
`EqViewModel.FabFilter.cs`, bouton + aide dans EqTab, dialogue
StorageProvider) ; version **3.1.0**, 43 modules 🔒, **458/458 tests**
(237 Core + 142 Audio + 79 App). Limites documentées : 24 bandes actives
max (précision « Ultra » = 30 bandes → réduire à « Fine » ou désactiver
des bandes avant export).

**Les trois projets sont intégralement recodés sous TRIADE** (vagues 1-7 du
plan) :

- **Vagues 1-4 — RoomEq.Core** (25 modules) : Dsp (FFT radix-2, fenêtres,
  biquads RBJ TDF2 par bloc, bruits Kellet, ESS forme fermée, Welch/octaves,
  mixage), Eq (bandes, presets, chaîne temps réel sans lock ni allocation,
  liaisons preset↔périphérique M-EQ-04), Calibration (déconvolution Farina
  avec seuil de détection 1000× — D8 —, cibles, planificateur anti-doublons,
  multi-signaux médiane+dispersion, précisions, designer, 36 profils micro),
  Headphones (AutoEq, **321 modèles** — D9), Io (saveur test/public, journal,
  télémétrie Drive, signaux SHA-256, magasins).
- **Vague 5 — RoomEq.Audio** (6 modules) : capture WASAPI/AUHAL avec repli de
  format, moteur de lecture + ISampleProvider, routage complet (RingBuffer
  SPSC, carte virtuelle RoomEq + boucle WASAPI, volume Windows, PolicyConfig,
  watcher, pilote pnputil, mixer.json), pont ASIO (miroir du protocole C
  verrouillé par tests), pont macOS CoreAudio, moteur de mesure mono +
  multi-signaux.
- **Vague 6 — RoomEq.App** (5 modules) : shell Avalonia 11, ViewModels
  décomposés par concern (< 300 lignes/fichier), 8 contrôles personnalisés,
  6 onglets ; 2 vrais bogues v2 corrigés (ASIO sans pilote, garde-fou preset
  plat) + FilePath de la tranche Lecteur persisté.
- **Vague 7 — Finalisation** : audit de parité v2.2.2 point par point
  (`docs/PARITE-v2.2.2.md`, 14/14 familles du README, empreintes md5 des
  données embarquées vérifiées), D9 casques 255→321, D10 cristallisation
  (tag `v3.0.0-dev.1`, 42 modules 🔒, packaging `scripts/package_v3.sh`, CI
  `.github/workflows/build.yml`), `LISEZMOI.txt`, télémétrie régénérable
  (`scripts/gen_telemetrie.py`).

**Chiffres (cristallisation v3.0.2)** : 42/42 modules 🔒 · **437/437 tests verts** (220 Core + 142
Audio + 75 App — legacy : 228) · build **0 erreur 0 avertissement**.

**Reste à faire** (hors recodage) :
- **Validation utilisateur sur machine Windows réelle** (checklist des 14
  familles, `docs/PARITE-v2.2.2.md` § 1) : installer le pilote scellé
  (`RoomEqDriverSetup-2.2.2.exe`), lancer `RoomEq.exe` / `RoomEq-test.exe`
  du zip 3.0.2. Tout écart constaté est traité en **patch 3.0.x à contrat
  stable** (D12-D14) — les tags ne sont jamais déplacés (v3.0.0 : tests CI
  Windows rouges ; v3.0.1 : packaging CI rouge ; v3.0.2 : correctifs
  portabilité, binaire identique, release CI publiée).
- **Vague 8 (installateur) PRÉPARÉE mais non lancée** (D11) : specs
  `I/installateur/` (M-INS-01..08, verrous 0x6F8 / 0xE0000205 / libellés
  pnputil) — lancement conditionné à la validation v3 + décision de
  signature + environnement Windows pour R5.

## 2. Méthode de travail (protocole TRIADE appliqué ici)

1. Lire ce fichier, `CARTE.md`, `docs/DECISIONS.md`, `docs/PLAN_RECODAGE.md`.
2. Choisir le module suivant (ordre des vagues) ; lire sa spec `I/**` si elle
   existe, sinon l'écrire d'après la legacy (`roomeq-git-ref/…`) AVANT le code.
3. Recoder ; porter les tests legacy correspondants + en ajouter.
4. **Verify-then-commit** : build 0/0 + tests 100 % verts, sinon on ne committe pas.
5. Mettre à jour la ligne du module dans `CARTE.md` (⬜→🚧→✅).
6. `scripts/sync_backup.sh` (commit + GitHub + Drive).
7. Consigner dans `WORKLOG.md`.

## 2bis. RÈGLE PERMANENTE — livraison d'une version (D22)

> **Une version n'est LIVRÉE que lorsque TOUT ceci est vert, dans cet ordre.**
> Aucune exception, aucun « je synchroniserai plus tard » : une version non
> synchronée n'est PAS livrée — l'utilisateur ne peut pas la considérer comme
> disponible.

1. **Tests d'abord** (R5) : build 0 erreur / 0 avertissement + suites
   xUnit 100 % vertes (+ suites natives si `natif/` a changé) —
   `scripts/sync_backup.sh` les déclenche et **refuse le commit** sinon.
2. **Git** : commit avec message significatif + push GitHub
   (branche `v3-recodage` + tags — un tag `v*` déclenche la release CI).
3. **Drive** : miroir complet via `scripts/drive_sync_roomeq.py` (dépôt,
   bundle git, livrables de `download/` → `Equalizer/RoomEq-v3/Livrables`).
4. **Archivage des anciennes versions** (automatique, dans le script de
   synchro) : au premier niveau de `Livrables` il ne reste que **la dernière
   version de chaque produit** (appli, VST, AU) + les fichiers non versionnés
   (`RoomEq-win-Setup.exe`, LISEZMOI) ; toute version antérieure est
   **déplacée** dans `Livrables/Archive` — jamais supprimée (retour arrière
   toujours possible). But : **une seule version visible par produit**,
   zéro confusion sur le fichier à télécharger.
5. **Vérification** : relire la sortie du script (ligne « Premier niveau de
   "Livrables" » = exactement la version courante par produit) et
   `git log`/`git ls-remote` alignés. En cas d'échec (jeton, réseau) :
   livrable marqué **NON LIVRÉ** dans `WORKLOG.md`, et la session suivante
   reprend la synchro en priorité absolue.

## 3. Décisions structurantes (détail dans docs/DECISIONS.md)

- **D1** périmètre : application C# ; natifs (pilote, ASIO, HAL macOS,
  installateurs) conservés scellés v2.2.2 — voir `natif/REFERENCE-NATIF.md`.
- **D2** stack conservée (.NET 8/Avalonia/NAudio), code réécrit.
- **D3** ordre : Core → Audio → App.
- **D4** gouvernance I/B/C : spec I par module, code B versionné, Carte C.
- **D5** tests = contrat exécutable de la couche I.
- **D6** base v2.2.2, cible v3.0.0.
- **D7** sauvegarde systématique.
- **D8** seuil de détection du balayage 8×→1000× (rejette le bruit pur).
- **D9** parité des données embarquées : casques portés à 321 (fichier v2.2.2
  repris tel quel, md5 vérifié).
- **D10** cristallisation `v3.0.0-dev.1` : 42 modules 🔒, packaging v3
  (`package_v3.sh`), CI GitHub Actions sans jobs natifs.
- **D11** reverse-specs de l'installateur scellé : `I/installateur/`, 8 modules
  M-INS-01..08 (« ⬜ spec prête »), conditions de lancement de la vague 8.
- **D12** cristallisation finale `v3.0.0` (ordre du développeur, 2026-10-26) :
  retrait du suffixe `-dev.1`, livrables `RoomEq-3.0.0-*` ; les écarts terrain
  éventuels passent en patchs 3.0.x à contrat stable.
- **D13** CI Windows rouge sur v3.0.0 (3 tests DiagnosticsTests, verrou
  fichier) : correctif tests-only (portage du `ReadLogShared` legacy),
  publication `v3.0.1` — tags immuables, specs I inchangées.
- **D14** portabilité CI du packaging : `rg` → `grep -E` (absent des
  runners), exclusion du dossier de sortie du zip source ; reproducteur
  `simulate_ci_packaging.sh` ; publication `v3.0.2`.
- **D15** « optimisation maximale » / fusion de modules / natif machine :
  requête refusée (R2/R4/R5, D1), mesure substituée — harnais `perf/` : thread
  audio à 152× temps réel et 0 allocation mesurés (2 vCPU partagés) ; aucun
  problème de performance à corriger, `src/` intact.
- **D23** parité fonctionnelle AU ↔ VST : banque casques + écriture JSON
  déplacées dans le cœur C++ partagé (`roomeq::bank`, `roomeq::preset::ToJson`
  — ports de RoomEq.Core, consommés par les DEUX greffons), couche d'édition
  portable `natif/roomeq-au/edit/` testée Linux, AU v3.6.0 (édition à la
  souris, banque, VU, auto-chargement, journal).
- **D22** règle permanente de livraison (§ 2bis) : synchro Git + Drive
  **obligatoire** à chaque livraison + **archivage automatique des anciennes
  versions** dans `Equalizer/RoomEq-v3/Livrables/Archive` — au premier niveau,
  une seule version visible par produit (appli, VST, AU), zéro confusion.
  Mécanisme dans `scripts/drive_sync_roomeq.py` (familles de noms + tri de
  versions, déplacement jamais suppression, idempotent). Entrée en vigueur
  immédiate : premier archivage effectué à l'adoption (24 anciens fichiers,
  session 16).
- **D24** EQ système par APO (M-AUD-09) : le DSP RoomEq s'installe dans le
  moteur audio Windows (audiodg, position EFX) — aucun pilote, aucune
  signature, aucune dépendance tierce, latence nulle (détail D24).
- **D25** console allégée : retrait des tranches « entrée/sortie » et du
  routage par carte virtuelle sur Windows (l'EQ système les remplace) ;
  console = sources internes uniquement ; anti double-correction
  (`ApoOnMaster` → bypass master) ; migration des défauts + suppression du
  pilote hérité ; macOS inchangé (détail D25).

## 4. Pièges et découvertes de la session 1

- Le miroir Drive des sources (`roomeq-legacy/src-drive`) contient des
  **doublons** (versions v1.x alongside v2.x) : la référence fiable est le
  **clone git** `roomeq-git-ref` (CI verte).
- GitHub était à **v2.2.2** (deff25c), plus récent que le dernier worklog Drive
  (v2.2.1) : toujours faire `git log` sur la référence.
- Affichage terminal : les séquences `[m`/`[i` dans les sources C#
  (`freqs[mid]`, `freqs[i]`) sont avalées comme échappements ANSI par bash —
  **le code est sain**, vérifier avec un lecteur de fichiers avant de conclure
  à une corruption.
- Legacy adoptée telle quelle (spécifiée puis vérifiée, pas réécrite) :
  TelemetryConfig, SignalPack, SignalStore, SignalCatalogClient,
  DriveLogUploader — déjà conformes et sécurisés (v1.9-v2.0).
- Améliorations v3 documentées : préampli appliqué dans EqChain.Process,
  SanitizeFileName multi-plateformes, DiagnosticsLog.OverrideDir, D8.

## 5. Environnement

```bash
export PATH="$HOME/.dotnet:$PATH"
cd /home/z/my-project/roomeq/src
dotnet build RoomEq.sln -c Debug          # 0 erreur 0 avertissement attendu
dotnet test ../tests/RoomEq.Core.Tests/RoomEq.Core.Tests.csproj    # 220 verts
dotnet test ../tests/RoomEq.Audio.Tests/RoomEq.Audio.Tests.csproj  # 142 verts
dotnet test ../tests/RoomEq.App.Tests/RoomEq.App.Tests.csproj      # 75 verts
bash ../perf/run.sh                        # marges temps réel (D15)
bash ../scripts/package_v3.sh             # livrables : zips test/public/source
bash ../scripts/sync_backup.sh            # fin de lot ET à chaque livraison (D22) : tests + commit + GitHub + Drive + archivage Livrables
```

Jetons Drive/GitHub : `../triade/.gdrive/` (voir `triade/REPRISE_PROJET.md` du
dépôt TRIADE pour la rotation). Dossier Drive du projet : « Equalizer/RoomEq-v3 ».
