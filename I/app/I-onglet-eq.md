# I-APP-04 — Onglet Égaliseur

> Module **M-APP-04** · couche I (intention) · projet RoomEq.App
> Version 1 — spec écrite AVANT le code (vague 6).

## Rôle

L'éditeur de l'égaliseur principal : preset courant (bandes éditables +
préampli), bibliothèque de presets sauvegardés, bibliothèque de
corrections casques AutoEq, import de la courbe de calibration, liaison
preset → sortie, et courbe composite interactive (poignées déplaçables).

## Décomposition (chaque fichier < 300 lignes)

| Fichier | Contenu |
|---|---|
| `EqViewModel.cs` | Preset courant, presets sauvegardés, import calibration, liaison sortie, courbe |
| `HeadphonePickerViewModel.cs` | Sélecteur casques : marques, modèles, recherche, application |
| `BandViewModel.cs` | Une bande d'EQ éditable (panneau + poignées) |

## Contrat observable

- **Preset courant** : `PresetName`, `PreampDb`, `Bands`
  (`ObservableCollection<BandViewModel>`), `SelectedIndex`, `CurveInfo`,
  événement `CurveChanged` + `CurrentCurve()`.
- **Presets sauvegardés** : `SavedPresets`, `SelectedSavedPreset`,
  `NewPresetName`, `PresetStatus` ; commandes `SavePreset`,
  `LoadSelectedPreset`, `DeleteSelectedPreset`, `RenamePreset`.
- **Casques** : sous-ViewModel `Headphone` — `HeadphoneBrands`,
  `SelectedHeadphoneBrand`, `HeadphoneModels`,
  `SelectedHeadphoneModel`, `HeadphoneSearch`, `HeadphoneStatus`,
  `CanApplyHeadphone`, `HeadphoneCatalogText`, commande
  `ApplyHeadphonePreset` ; événement remontant `HeadphonePresetApplied`.
- **Import calibration** : `PrecisionOptions` + `SelectedPrecision`
  (`CorrectionPrecisionCatalog`), `HasCalibrationCurve`, `ImportStatus`,
  commande `ImportCalibrationCurve` ; source fournie/retirée par le shell
  via `SetCalibrationSource(CorrectionResult?)`.
- **Liaison sortie** : `LinkTargetText`, `LinkPresetStatus`, commande
  `LinkPresetToOutput`, événements `LinkPresetRequested`,
  `HeadphonePresetApplied`.
- **Bandes** : `AddBand`, `RemoveSelected`, `RemoveBand(band)`,
  `Flatten` ; interactions graphe : `OnBandDragged(index, freq, gain)`
  (arrondis 0,1), `OnBandQChanged(index, facteur)` (Q borné 0,1–20).
- `Init(engine)` : charge un preset plat, la liste des presets, la
  bibliothèque casques ; toute modification est poussée dans
  `engine.Eq` (reconfiguration sans clic audio).

## Comportements

1. **Chargement de preset** : remplace les bandes, pousse dans la chaîne,
   recalcule la courbe (360 points, 20 Hz–20 kHz), rafraîchit la liste.
2. **Bande éditable** : fréquence/gain/Q/type/activée, notifie à chaque
   changement → push audio + recalcul courbe.
3. **Sauvegarde/chargement/suppression/renommage** via `PresetStore`
   (noms sanitizés, renommage refusé si cible existante, statut explicite).
4. **Casques** : la recherche couvre tout le catalogue (marque ignorée
   pendant la recherche), la marque filtre sinon ; l'application remplace
   les bandes (préampli compris), notifie `HeadphonePresetApplied`.
5. **Import calibration** : `CorrectionDesigner.DesignFromCalibrationCurve`
   à la précision choisie ; sans courbe disponible → message explicite.
6. **Renommage** : met à jour le nom affiché si le preset courant est
   celui renommé.
7. `Flatten` remet aussi le préampli à 0 dB.

## Invariants

1. Le preset poussé dans la chaîne est TOUJOURS reconstruit depuis les
   vues de bandes (`BuildPreset`) — une seule source de vérité.
2. La suppression passe par INDEX (l'égalité d'objet ne retire rien).
3. `PreampDb` est appliqué à la chaîne par `SetPreamp` immédiatement.
4. La bibliothèque casques est en lecture seule (aucune écriture disque).

## Tests

- Bandes : édition notifiée, `ToModel` aller-retour, `FreqText`
  (Hz/kHz).
- Presets : sauvegarde/liste/chargement/suppression/renommage (avec
  magasin en répertoire de test), nom vide → « Sans nom ».
- Sélecteur casques : marques peuplées, recherche qui couvre le catalogue,
  sélection conservée au filtrage, application → preset chargé +
  événement émis.
- Import : sans courbe → statut explicite ; avec courbe factice →
  bandes générées selon la précision.
- Courbe : recalcul après chargement, `CurveInfo` cohérent.
