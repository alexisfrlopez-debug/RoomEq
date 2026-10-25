# PLAN DE RECODAGE — RoomEq v3.0.0 sous protocole TRIADE

> Document de pilotage du recodage complet. Chaque vague suit le cycle TRIADE :
> **spec I → code → tests verts → marquage Carte → commit + sauvegarde (R5/D7)**.

## Objectif

Recoder entièrement l'application RoomEq (les trois projets C#) en appliquant le
protocole TRIADE : chaque module est défini par une intention diffable (I),
implémenté par du code versionné (B) et cartographié à adresse stable (C).
Critère de non-régression : **parité fonctionnelle avec la v2.2.1**, suite de
tests ≥ couverture legacy, build 0 erreur 0 avertissement.

## Vagues

### Vague 1 — Fondations Triade (cette session, en cours)
- Dépôt `roomeq/` initialisé : `I/` `src/` `tests/` `docs/` `natif/` `scripts/`
- `CARTE.md` (37 modules + 4 natifs scellés), `docs/DECISIONS.md` (D1–D7),
  ce plan, `PROTOCOLE-AGENT.md`, `REPRISE.md`
- SDK .NET 8 installé, solution squelette qui compile

### Vague 2 — RoomEq.Core : Dsp + Eq (cœur pur)
Modules M-DSP-01..07, M-EQ-01..03 — algorithmes : FFT radix-2 Cooley-Tukey,
fenêtres Hann/Blackman-Harris/rectangulaire, biquads RBJ (6 types), bruits
blanc/rose(Kellet)/brun/violet + sinus, ESS logarithmique Farina, analyse
spectrale (octaves, SPL), chaîne EQ temps réel avec reconfiguration sans clic.
Tests de référence : `CoreDspTests.cs` legacy (321 lignes) + `SweepTests` +
`EqChainRateTests` + `ChannelMixerTests`.

### Vague 3 — RoomEq.Core : Calibration + Headphones
M-CAL-01..08, M-HPH-01 — analyse de balayage (déconvolution), courbe cible,
planificateur de bandes automatique, analyse multi-signaux, métriques de
précision, design de correction, profils micro (MicLibrary.json embarqué),
bibliothèque presets casque AutoEq (255 modèles, JSON embarqué).

### Vague 4 — RoomEq.Core : Io
M-IO-01..05 — saveur test/public, journal diagnostic, télémétrie Drive,
magasin de signaux téléchargés (validation SHA-256), magasins de réglages.

### Vague 5 — RoomEq.Audio
M-AUD-01..06 — capture WASAPI, lecture (WASAPI partagé/exclusif), routage
mixeur virtuel→physique, pont ASIO (shared memory), pont macOS CoreAudio,
moteur de mesure de pièce. Dépend des natifs scellés N-DRV-01/N-ASI-01.

### Vague 6 — RoomEq.App
M-APP-01..05 — shell Avalonia, onglets Calibration/Mixage/EQ/Micro/Analyseur/
ASIO, ViewModels découplés (taille cible < 300 lignes par ViewModel), contrôles
personnalisés (courbes, VU-mètres, éditeur EQ).

### Vague 7 — Finalisation
- Solution complète 0 erreur 0 avertissement, suite complète verte
- Tag `v3.0.0-dev.1`, marqueurs Carte 🔒
- Parité v2.2.1 vérifiée point par point (checklist README legacy)
- Packaging : réutiliser les scripts CI/packaging legacy adaptés

## Règles de travail (extraites du protocole)

1. **Jamais de B nu** : aucun fichier de `src/` sans module cartographié et spec I.
2. **Intention d'abord** : la spec I du module est écrite AVANT le code.
3. **Verify-then-commit** : `dotnet build` 0/0 + `dotnet test` 100 % verts
   avant chaque commit.
4. **Patchs à contrat stable** : l'API publique d'un module ne change pas sans
   bump de la version de sa spec I.
5. **Sauvegarde systématique** : `scripts/sync_backup.sh` après chaque vague.
6. **Legacy en lecture seule** : `roomeq-legacy/` n'est jamais modifié ; il sert
   de référence et de source des tests.

## Environnement

- SDK : `export PATH="$HOME/.dotnet:$PATH"` (8.0.x)
- Build : `cd src && dotnet build RoomEq.sln -c Debug`
- Tests : `dotnet test tests/RoomEq.Core.Tests` (etc.)
- Jetons : `../triade/.gdrive/` (GitHub + Drive) — jamais commités
