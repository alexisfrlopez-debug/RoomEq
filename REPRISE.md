# REPRISE — RoomEq v3 (recodage Triade)

> **À lire en premier au début de chaque session.** Ce fichier est le point
> d'entrée : état, restauration, règles, prochaine étape.

## 0. Résumé express

| | |
|---|---|
| **Projet** | RoomEq **v3.0.0 finale** — recodage complet de l'application sous protocole TRIADE, **cristallisé** |
| **Base fonctionnelle** | v2.2.2 (GitHub `alexisfrlopez-debug/RoomEq` @ `deff25c`) |
| **Ce dépôt** | `/home/z/my-project/roomeq` — branche `main` (recodage), miroirs Drive + GitHub `v3-recodage` |
| **Référence legacy** | clone git `/home/z/my-project/roomeq-git-ref` + miroir Drive `/home/z/my-project/roomeq-legacy` |
| **SDK** | `export PATH="$HOME/.dotnet:$PATH"` (8.0.427 ; réinstaller : `curl -sSL https://dot.net/v1/dotnet-install.sh \| bash -s -- --channel 8.0 --install-dir "$HOME/.dotnet"`) |
| **Vérifier** | `cd src && dotnet build RoomEq.sln -c Debug` puis les 3 suites (`dotnet test ../tests/RoomEq.*.Tests`) — 437 verts |
| **Sauvegarde** | `scripts/sync_backup.sh` — tests + commit + push GitHub + miroir Drive (**à chaque lot de travail**, D7) ; les **livrables** (zips `RoomEq-*` de `download/`) sont téléversés dans `Equalizer/RoomEq-v3/Livrables` par le même miroir (resumable, idempotent MD5) |
| **Jetons** | `../triade/.gdrive/` (GitHub + Drive OAuth) — jamais commités |

## 1. État (fin session 6 — recodage TERMINÉ, cristallisé v3.0.0 finale)

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

**Chiffres** : 42/42 modules 🔒 · **437/437 tests verts** (220 Core + 142
Audio + 75 App — legacy : 228) · build **0 erreur 0 avertissement**.

**Reste à faire** (hors recodage) :
- **Validation utilisateur sur machine Windows réelle** (checklist des 14
  familles, `docs/PARITE-v2.2.2.md` § 1) : installer le pilote scellé
  (`RoomEqDriverSetup-2.2.2.exe`), lancer `RoomEq.exe` / `RoomEq-test.exe`
  du zip 3.0.0. Tout écart constaté est traité en **patch 3.0.x à contrat
  stable** (D12) — le tag n'est jamais déplacé.
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
bash ../scripts/package_v3.sh             # livrables : zips test/public/source
bash ../scripts/sync_backup.sh            # fin de lot : commit + GitHub + Drive
```

Jetons Drive/GitHub : `../triade/.gdrive/` (voir `triade/REPRISE_PROJET.md` du
dépôt TRIADE pour la rotation). Dossier Drive du projet : « Equalizer/RoomEq-v3 ».
