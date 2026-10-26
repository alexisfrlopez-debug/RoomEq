# I-EQ-04 — Liaisons preset ↔ périphérique de sortie

> Module **M-EQ-04** · couche I (intention) · projet RoomEq.Core/Eq
> Version 1 — spec écrite AVANT le code (vague 6). Comblement : ce module
> existait en v2.2.2 (`Core/Eq/DevicePresetStore.cs`) mais avait été oublié
> lors des vagues 2-4 du recodage ; l'onglet Mixage en dépend.

## Rôle

Mémoriser, pour chaque périphérique de sortie audio (identifié par son ID
WASAPI/CoreAudio), quel preset d'égaliseur lui est associé. C'est la
persistance de la politique « preset adapté à la sortie » (M-APP-01) :
l'application peut ainsi réappliquer automatiquement la bonne correction
quand un casque est rebranché ou quand la sortie master change.

## Modèle

- `DevicePresetKind` (énumération) — origine de la liaison :
  - `Manual` : choix explicite de l'utilisateur (prioritaire sur toute
    détection automatique) ;
  - `Calibration` : preset enregistré à l'issue d'une calibration réalisée
    sur ce périphérique (prioritaire sur les presets usine) ;
  - `None` : aucun preset pour ce périphérique, même automatique.
- `DevicePresetBinding` (classe sérialisable) :
  - `DeviceId` (non vide, requis), `DeviceName` (libellé informatif) ;
  - `PresetClass` : `"user"` (magasin PresetStore, par nom) ou
    `"factory"` (bibliothèque usine, par identifiant) ;
  - `PresetRef` : nom ou identifiant référencé ;
  - `Kind` : la valeur d'énumération ci-dessus ;
  - `BoundAt` : horodatage de la liaison ;
  - `IsUsable` : `Kind != None && PresetRef` non vide.
- `DevicePresetStore` (classe) — magasin JSON à fichier unique :
  - Fichier : `<répertoire>/devicepresets.json`, format
    `{ "Version": 1, "Bindings": [ … ] }`, énumérations en chaînes,
    indenté ;
  - Constructeur par défaut : `%APPDATA%/RoomEq/devicepresets.json` ;
  - Constructeur alternatif `DevicePresetStore(string directory)` :
    répertoire imposé (tests, mode portable).

## Contrat (API publique, stable — tout changement = bump de version)

| Membre | Sémantique |
|---|---|
| `IReadOnlyDictionary<string, DevicePresetBinding> LoadAll()` | Toutes les liaisons indexées par `DeviceId` (comparaison insensible à la casse) ; dernier écrit gagne si doublon |
| `DevicePresetBinding? Get(string deviceId)` | Liaison du périphérique, `null` si aucune |
| `void Set(DevicePresetBinding binding)` | Écrit/remplace la liaison du périphérique ; lève `ArgumentException` si `DeviceId` vide |
| `bool Remove(string deviceId)` | Supprime la liaison (retour Auto) ; `true` si une liaison existait |
| `string Directory { get; }` / `string FilePath { get; }` | Emplacements réels du magasin |

## Invariants

1. **Un fichier JSON par magasin**, réécrit intégralement à chaque `Set` /
   `Remove` effectif (pas d'écriture si `Remove` ne trouve rien).
2. **Fichier illisible ou absent** : `LoadAll` repart d'un magasin vide —
   jamais d'exception vers l'appelant (l'audio ne doit pas être perturbé
   par un fichier corrompu).
3. **Cohérence insensible à la casse** des `DeviceId` partout (get/set/
   remove/loadall partagent le même comparateur).
4. Le répertoire est créé à l'écriture s'il n'existe pas.
5. La persistance ne modifie JAMAIS le champ `Version` lu (tolérance
   ascendante : champs inconnus ignorés).

## Tests de vérification (portés de la v2.2.2)

- Magasin vide → aucune liaison, `Get` renvoie `null`.
- `Set` puis `Get` : aller-retour complet (tous champs).
- `Set` sans `DeviceId` → `ArgumentException`.
- `Remove` ne supprime QUE la cible.
- Fichier corrompu → repli sur magasin vide sans lever.
- Liaison `None` persistée mais `IsUsable == false`.

## Notes d'implémentation

- Sérialisation : `JsonOpts.Default` du module I-IO-05 (énumérations en
  chaînes, indenté) pour l'homogénéité des magasins du projet.
- Ne dépend d'aucun autre module (pur JSON + fichiers) — testable sans
  périphérique audio.
