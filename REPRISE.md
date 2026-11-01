# REPRISE — RoomEq v3 (recodage Triade)

> **À lire en premier au début de chaque session.** Ce fichier est le point
> d'entrée : état, restauration, règles, prochaine étape.

## 0. Résumé express

| | |
|---|---|
| **Projet** | RoomEq **v3.5.0** — recodage complet TRIADE **cristallisé et publié**, + export FabFilter Pro-Q 3 (D17), + anti-boucle au routage (D18), + greffon AudioUnit Ableton (D19), + installation « 1 clic » (D20 : Setup.exe Windows Velopack + .pkg macOS), + greffon VST 2 32 bits Windows (D21) avec correctifs v3.4.1/v3.4.2 et **v3.5.0 : égaliseur éditable dans le greffon, banque casques embarquée (321 modèles), VU-mètre + diagnostics** |
| **Base fonctionnelle** | v2.2.2 (GitHub `alexisfrlopez-debug/RoomEq` @ `deff25c`) |
| **Ce dépôt** | `/home/z/my-project/workspace/RoomEq` (sandbox recyclée en session 11 : re-cloné depuis GitHub, branche `v3-recodage`) — miroirs Drive + GitHub `alexisfrlopez-debug/RoomEq` |
| **Référence legacy** | clone git `/home/z/my-project/roomeq-git-ref` + miroir Drive `/home/z/my-project/roomeq-legacy` |
| **SDK** | `export PATH="$HOME/.dotnet:$PATH"` (8.0.427 ; réinstaller : `curl -sSL https://dot.net/v1/dotnet-install.sh \| bash -s -- --channel 8.0 --install-dir "$HOME/.dotnet"`) |
| **Vérifier** | `cd src && dotnet build RoomEq.sln -c Debug` puis les 3 suites (`dotnet test ../tests/RoomEq.*.Tests`) — 461 verts ; greffon AU : `g++ -std=c++17 -Wall -Wextra -Inatif/roomeq-au/core natif/roomeq-au/core/roomeq/{dsp,preset,json}/*.cpp natif/roomeq-au/tests/test_main.cpp -o /tmp/t && /tmp/t natif/roomeq-au/tests/data` (118 ; gcc 14 : `-Wno-error=maybe-uninitialized`, faux positif scellé) ; greffon VST : `cmake -S natif/roomeq-vst -B build-vst && cmake --build build-vst && ./build-vst/roomeq_vst_tests natif/roomeq-au/tests/data && ./build-vst/roomeq_vst_host build-vst/RoomEqVst.so natif/roomeq-au/tests/data` (403 + 38) puis `natif/roomeq-vst/build-vst.sh` (DLL win32 ; Zig dans `~/tools/zig-*` ; si la bibliothèque casques change : `python3 scripts/gen_headphone_bank.py` d'abord) ; macOS : `bash natif/roomeq-au/build-macos.sh` (build + auval) |
| **Sauvegarde** | `scripts/sync_backup.sh` — tests + commit + push GitHub + miroir Drive (**à chaque lot de travail**, D7) ; les **livrables** (zips `RoomEq-*` de `download/`) sont téléversés dans `Equalizer/RoomEq-v3/Livrables` par le même miroir (resumable, idempotent MD5) |
| **Jetons** | `../triade/.gdrive/` (GitHub + Drive OAuth) — jamais commités |

## 1. État (fin session 14 — v3.5.0 : égaliseur éditable + banque casques + diagnostic)

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
bash ../scripts/sync_backup.sh            # fin de lot : commit + GitHub + Drive
```

Jetons Drive/GitHub : `../triade/.gdrive/` (voir `triade/REPRISE_PROJET.md` du
dépôt TRIADE pour la rotation). Dossier Drive du projet : « Equalizer/RoomEq-v3 ».
