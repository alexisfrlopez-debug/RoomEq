# WORKLOG — RoomEq v3 (recodage Triade)

Journal de travail du recodage. Une section par session, format TRIADE.
Copie de référence également maintenue dans le worklog principal
(`/home/z/my-project/worklog.md`).

---
Session : 1 — 2026-10-25/26
Agent : main (Z.ai)
Task : Reprise du projet RoomEq (dossier Drive « Equalizer ») : recodage
entier de l'application, optimisation et nettoyage selon le protocole TRIADE.

Work Log:
- Restauré le contexte complet : listing récursif du Drive « Equalizer »
  (518 entrées), téléchargement miroir idempotent (284 fichiers, 7,9 Mo —
  doublons v1.x écartés en gardant le plus récent), lecture
  d'INSTRUCTIONS-REPRISE.md (v1.9.0) et des worklogs Drive (v2.0→v2.2.1).
- Découvert que GitHub est à **v2.2.2** (deff25c : correctif interop
  SP_DEVINFO_DATA 0x6F8) — plus récent que le dernier worklog Drive → cloné
  `roomeq-git-ref` comme référence code (CI verte) ; le miroir Drive
  `roomeq-legacy/` ne sert que pour les documents hors dépôt.
- Fausses alertes de corruption écartées : les affichages « freqsid] » en bash
  sont des séquences ANSI avalées (`[m` de `freqs[mid]`) — vérifié au lecteur
  de fichiers.
- Installé le SDK .NET 8.0.427 ; créé le dépôt `/home/z/my-project/roomeq`
  avec la structure TRIADE : `I/` (specs par module), `src/`, `tests/`,
  `CARTE.md` (37 modules + 4 natifs scellés), `docs/DECISIONS.md` (D1-D7),
  `docs/PLAN_RECODAGE.md` (7 vagues), `natif/REFERENCE-NATIF.md`.
- **Vague 2 — Dsp + Eq** : specs I écrites puis code recodé : FFT radix-2
  (bit-reversal + papillons par étapes), fenêtres (Hann/BH/rect + protection
  n=1), biquads RBJ en **Transposed Direct Form II** + Process(Span) + MagnitudeAt,
  bruits (blanc/rose Kellet/brun/violet/sinus), balayage ESS (phase forme
  fermée), SpectrumTools (Welch/octaves/lissage O(n)/InterpLog/Peak-RMS),
  ChannelMixer (repli robuste), EqBand/EqPreset/EqChain (reconfiguration par
  référence atomique, **préampli appliqué dans Process** — amélioration),
  CurvePoint partagé. 86 tests verts après correction de 4 tests fautifs
  (Hann(8) : sommet non échantillonné = 0,9505 ; spec corrigée en conséquence).
- **Vague 3 — Calibration + Headphones** : specs puis recodage de TargetCurve
  (3 cibles), SweepAnalyzer (Farina complet), AutoBandPlanner (glouton
  anti-doublons à zones d'exclusion), MultiSignalAnalysis (médiane +
  dispersion + poids de confiance), CorrectionPrecision (4 niveaux, valeurs
  exactes), CorrectionDesigner (Design/FromAligned/FromCalibration),
  MicProfile (+ parseur tolérant), MicProfileLibrary (ressource intégrée),
  MeasurementSignalCatalog, HeadphonePresetLibrary (AutoEq 255 modèles,
  MatchDeviceName normalisé accents). 64 tests verts.
- **Diagnostic empirique de la porte SNR** (mini-projet console temporaire) :
  bruit pur = ratio 186, bruit passe-bas réaliste = 9,2, balayage réel =
  1,2e7 → le seuil 8× de la v2.2.2 laissait passer le bruit seul (bandes
  absurdes au lieu du message d'aide). **Décision D8** : seuil relevé à
  1000× (4 décades de marge de chaque côté), spec + tests à l'appui.
- **Vague 4 — Io** : AppFlavor (silence par défaut) et DiagnosticsLog recodés
  (avec **OverrideDir** qui ferme le writer au changement + ResetForTests) ;
  TelemetryConfig, SignalPack, SignalStore, SignalCatalogClient,
  DriveLogUploader **adoptés tels quels** (déjà conformes aux specs que j'ai
  écrites à partir d'eux, sécurisés en v1.9-v2.0, testés — la réécriture
  n'apporterait rien) ; Stores recodé avec **SanitizeFileName
  multi-plateformes** (union fixe Windows+Unix : la v2.2.2 dépendait de
  Path.GetInvalidFileNameChars() de l'OS hôte — défaut révélé par les tests
  sous Linux). 62 tests verts.
- Outillage de sauvegarde (D7) : `scripts/drive_sync_roomeq.py` (miroir MD5
  idempotent + bundle git) et `scripts/sync_backup.sh` (tests → commit → push
  GitHub branche v3-recodage → miroir Drive « Equalizer/RoomEq-v3 »).
- Documents de reprise : REPRISE.md (état + méthode + pièges), README.md,
  REFERENCE-NATIF.md, Carte et plan mis à jour (vagues 1-4 ✅).
- Commits : 6e3efc5 (Dsp+Eq+Calibration+Headphones, 150 tests) puis 0d04fce
  (Io, 212 tests) + commit de clôture.

Stage Summary:
- **RoomEq.Core v3 entièrement recodé sous TRIADE** : 24/24 modules ✅,
  212/212 tests verts, build 0 erreur 0 avertissement, 25 specs I écrites
  (une par module) AVANT le code, Carte à adresses stables tenue à jour.
- Améliorations documentées vs v2.2.2 : D8 (détection balayage), préampli
  EqChain, SanitizeFileName multi-plateformes, DiagnosticsLog injectable,
  PlanInfo dans CorrectionResult (le designer ne dépend plus du journal).
- Prochaine session : **vague 5 — RoomEq.Audio** (M-AUD-01..06, 5 259 lignes
  legacy : capture, lecture, routage/mixeur, pont ASIO, pont macOS, moteur de
  mesure) puis vague 6 — App. Checklist : REPRISE.md §2.
- Sauvegardes : GitHub branche v3-recodage + Drive « Equalizer/RoomEq-v3 »
  (fichiers + bundle git restaurable) + dépôt local.
