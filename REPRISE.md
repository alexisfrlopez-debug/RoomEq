# REPRISE — RoomEq v3 (recodage Triade)

> **À lire en premier au début de chaque session.** Ce fichier est le point
> d'entrée : état, restauration, règles, prochaine étape.

## 0. Résumé express

| | |
|---|---|
| **Projet** | RoomEq v3.0.0 — recodage complet de l'application sous protocole TRIADE |
| **Base fonctionnelle** | v2.2.2 (GitHub `alexisfrlopez-debug/RoomEq` @ `deff25c`) |
| **Ce dépôt** | `/home/z/my-project/roomeq` — branche `main` (recodage), miroirs Drive + GitHub `v3-recodage` |
| **Référence legacy** | clone git `/home/z/my-project/roomeq-git-ref` + miroir Drive `/home/z/my-project/roomeq-legacy` |
| **SDK** | `export PATH="$HOME/.dotnet:$PATH"` (8.0.427 ; réinstaller : `curl -sSL https://dot.net/v1/dotnet-install.sh \| bash -s -- --channel 8.0 --install-dir "$HOME/.dotnet"`) |
| **Vérifier** | `cd src && dotnet build RoomEq.sln -c Debug` puis `dotnet test ../tests/RoomEq.Core.Tests/RoomEq.Core.Tests.csproj` |
| **Sauvegarde** | `scripts/sync_backup.sh` — tests + commit + push GitHub + miroir Drive (**à chaque lot de travail**, D7) |
| **Jetons** | `../triade/.gdrive/` (GitHub + Drive OAuth) — jamais commités |

## 1. État (fin session 1)

**Terminé — RoomEq.Core v3 INTÉGRALEMENT recodé** (vagues 1-4 du plan) :

- **Vague 1** : structure Triade (I/ specs · src/ code · tests/ · CARTE.md ·
  docs/DECISIONS.md D1-D8 · docs/PLAN_RECODAGE.md), SDK .NET 8, solution.
- **Vague 2 — Dsp + Eq** (10 modules) : FFT radix-2, fenêtres, biquads RBJ en
  Transposed Direct Form II + traitement par bloc, bruits (Kellet), balayage ESS
  forme fermée, Welch/octaves/lissage, mixage canaux, bandes/presets/chaîne EQ
  temps réel sans lock ni allocation (préampli intégré).
- **Vague 3 — Calibration + Headphones** (9 modules) : courbes cibles,
  déconvolution Farina (seuil de détection relevé 8×→1000×, D8), planificateur
  glouton anti-doublons, croisement multi-signaux (médiane + dispersion),
  précisions, designer, profils micro + bibliothèque, presets casque AutoEq
  (255 modèles + reconnaissance de périphérique).
- **Vague 4 — Io** (5 modules) : saveur test/public, journal diagnostic,
  télémétrie Drive, catalogue de signaux téléchargeables (validation avant
  réseau + SHA-256), magasins presets/profils.

**Chiffres** : 24/24 modules Core ✅ · **212/212 tests verts** · build
**0 erreur 0 avertissement** · specs I écrites pour chaque module avant le code.

**Reste à faire** (vagues 5-7 du plan) :
- **Vague 5 — RoomEq.Audio** (M-AUD-01..06) : capture, lecture, routage/mixeur,
  pont ASIO, pont macOS, moteur de mesure. Legacy : 25 fichiers, 5 259 lignes.
- **Vague 6 — RoomEq.App** (M-APP-01..05) : shell Avalonia, onglets,
  ViewModels (cible < 300 lignes chacun), contrôles. Legacy : 27 fichiers,
  4 922 lignes.
- **Vague 7 — Finalisation** : tag v3.0.0-dev.1, parité v2.2.2 point par point,
  packaging.

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
dotnet test ../tests/RoomEq.Core.Tests/RoomEq.Core.Tests.csproj   # 212 verts
bash ../scripts/sync_backup.sh            # fin de lot : commit + GitHub + Drive
```

Jetons Drive/GitHub : `../triade/.gdrive/` (voir `triade/REPRISE_PROJET.md` du
dépôt TRIADE pour la rotation). Dossier Drive du projet : « Equalizer/RoomEq-v3 ».
