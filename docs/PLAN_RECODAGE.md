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

### Vague 1 — Fondations Triade ✅ (session 1)
- Dépôt `roomeq/` initialisé : `I/` `src/` `tests/` `docs/` `natif/` `scripts/`
- `CARTE.md` (37 modules + 4 natifs scellés), `docs/DECISIONS.md` (D1–D7),
  ce plan, `PROTOCOLE-AGENT.md`, `REPRISE.md`
- SDK .NET 8 installé, solution squelette qui compile

### Vague 2 — RoomEq.Core : Dsp + Eq ✅ (session 1, 86 tests)
Modules M-DSP-01..07, M-EQ-01..03 — algorithmes : FFT radix-2 Cooley-Tukey,
fenêtres Hann/Blackman-Harris/rectangulaire, biquads RBJ (6 types), bruits
blanc/rose(Kellet)/brun/violet + sinus, ESS logarithmique Farina, analyse
spectrale (octaves, SPL), chaîne EQ temps réel avec reconfiguration sans clic.
Tests de référence : `CoreDspTests.cs` legacy (321 lignes) + `SweepTests` +
`EqChainRateTests` + `ChannelMixerTests`.

### Vague 3 — RoomEq.Core : Calibration + Headphones ✅ (session 1, 64 tests)
M-CAL-01..08, M-HPH-01 — analyse de balayage (déconvolution), courbe cible,
planificateur de bandes automatique, analyse multi-signaux, métriques de
précision, design de correction, profils micro (MicLibrary.json embarqué),
bibliothèque presets casque AutoEq (255 modèles, JSON embarqué).

### Vague 4 — RoomEq.Core : Io ✅ (session 1, 62 tests)
M-IO-01..05 — saveur test/public, journal diagnostic, télémétrie Drive,
magasin de signaux téléchargés (validation SHA-256), magasins de réglages.

### Vague 5 — RoomEq.Audio ✅ (session 2, 142 tests)
M-AUD-01..06 — specs I/audio/ écrites AVANT le code, puis recodage intégral :
capture WASAPI/AUHAL avec repli de format, moteur de lecture + briques
ISampleProvider (gain/canaux/tap/EQ/générateur/boucle), routage complet
(RingBuffer SPSC, tranches, bus master, anti-larsen, carte virtuelle RoomEq +
boucle WASAPI, volume Windows, PolicyConfig, watcher, pilote pnputil/Setup,
persistance mixer.json), pont ASIO (miroir exact du protocole C, transport
multiplateforme), pont macOS (P/Invoke CoreAudio résolus au premier appel),
moteur de mesure (mono + multi-signaux). Natifs scellés N-DRV-01/N-ASI-01
consommés via les mêmes points d'entrée (D1). Dépend de RoomEq.Core v3.
Améliorations v3 : ConsoleWasapiOutput fusionné dans WasapiOutput(latence),
MasterBus/StereoToOutput internes testables, transport du pont discriminé par
test de plateforme (plus de catch PlatformNotSupportedException), Volume/Loop
du PlaybackEngine atomiques (Volatile), PlaybackStopped levé par la sortie
CoreAudio à l'arrêt.

### Vague 6 — RoomEq.App ✅ (session 3, 75 tests)
M-APP-01..05 + comblement M-EQ-04 (DevicePresetStore, oublié des vagues 2-4) :
specs I écrites AVANT le code (6 nouvelles : 1 eq + 5 app), puis recodage
complet du projet Avalonia 11 (csproj, thème sombre, assets, manifest) :
shell + MainWindow (barre supérieure, onglets, OSD volume, raccourcis
Ctrl+Alt+↑/↓/M), ViewModels décomposés par concern (chaque fichier < 300
lignes : MainViewModel + OutputPresetCoordinator + TestSignalMenu extraits du
shell ; MixerViewModel en 5 partiels Routing/Volume/Driver/Persist ;
ChannelViewModel/TrackEqViewModel séparés ; CalibrationOptions +
LiveMicMonitor extraits de la calibration ; HeadphonePickerViewModel extrait
de l'EQ ; MicViewModel en 2 partiels), contrôles personnalisés (PlotControl
log/dB, VuMeter, EqControl à poignées, MicCurveEditor, dialogues d'aide/
saveur/catalogue), vues des 6 onglets. Améliorations v3 documentées :
fichier de la tranche Lecteur restauré au démarrage (StoredChannel.FilePath),
message d'aide ASIO affiché même sans pilote (bogue v2 : SelectedDriver null
→ pas de PropertyChanged → Info vide), garde-fou « Lier le preset » étendu au
preset plat (« Plat » échappait au contrôle v2), interfaces étroites
IOutputPresetEqView/IOutputPresetMixerView rendant la politique preset↔sortie
testable sans périphérique. Vérification R5 : build 0/0, 436 tests verts
(219 Core + 142 Audio + 75 App).

### Vague 7 — Finalisation (PROCHAINE SESSION — reprendre ici)
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
