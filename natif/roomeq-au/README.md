# RoomEq AU — greffon AudioUnit « RoomEq Correction » (.component)

Composant natif **N-AU-01** (spec `I/audio/I-greffon-au.md` v2, décisions
D19/D23) : applique la correction acoustique RoomEq **dans Ableton Live** (et
tout DAW macOS acceptant les AudioUnits), à partir d'un preset exporté de
l'application — ou **directement dans le greffon** depuis la v3.6.0.

```
Application RoomEq (mesure, réglage, export)
        │  preset .json (format de l'application) ou .ffp (FabFilter Pro-Q 3)
        ▼
RoomEq.component — RoomEq: Correction (aufx / Rmeq / Rmeq)
   • mêmes biquads RBJ que l'application (parité bit à bit, tests natifs)
   • ÉGALISEUR MODIFIABLE directement à la souris (v3.6.0)
   • banque casques/écouteurs EMBARQUÉE : 321 modèles AutoEq (v3.6.0)
   • VU-mètres entrée/sortie + version affichée (diagnostic du flux)
   • auto-chargement du preset le plus récent de l'application
   • paramètre « Préampli » automatisable, bypass standard du DAW
```

## Installer (1 clic — D20)

Les **releases GitHub** fournissent `RoomEq-AU-<version>-macos.pkg` :
double-clic → `RoomEq.component` est installé dans
`/Library/Audio/Plug-Ins/Components` (tous les utilisateurs ; le
postinstall retire l'éventuelle quarantaine et force le re-scan de la base
des composants — Ableton voit le greffon immédiatement). Aucune ligne de
commande. Alternative sans .pkg : le zip `RoomEq-AU-<version>-macos.zip`
(voir « Construire » ci-dessous pour la copie manuelle).

## Construire (macOS)

Prérequis : Xcode (outils de ligne de commande) et CMake ≥ 3.22.

```bash
bash natif/roomeq-au/build-macos.sh   # compile, teste, signe, installe, valide (auval)
```

Le script installe le bundle dans `~/Library/Audio/Plug-Ins/Components/`
(niveau utilisateur) puis le valide avec `auval` — l'outil ne voit que les
composants enregistrés. Le bundle produit est
`build-macos/RoomEq.component` ; un `.component` téléchargé depuis Internet
porte un attribut de quarantaine, à retirer :

```bash
xattr -cr ~/Library/Audio/Plug-Ins/Components/RoomEq.component
```

> La signature est **ad hoc** (aucun compte développeur nécessaire pour un
> usage personnel). Un `.component` téléchargé depuis Internet porte un
> attribut de quarantaine : la commande `xattr -cr` ci-dessus le retire.

## Utiliser dans Ableton Live

1. Activez les AudioUnits : Live › Préférences › Plug-ins › *Utiliser les
   périphériques Audio Unit*.
2. Le greffon apparaît comme **RoomEq: Correction** (Audio Effects).
3. Insérez-le sur une piste ou sur le master. **Dès l'insertion** le preset
   le plus récent du magasin de l'application
   (`~/.config/RoomEq/presets`) est chargé automatiquement (les presets
   « plats », sans effet audible, sont sautés) ; l'état du projet Live
   reste maître une fois la session sauvegardée.
4. Sinon, chargez une correction :
   - un `.json` exporté de RoomEq (onglet Égaliseur › enregistrer le
     preset ; bouton **Charger un preset…** ou glisser-déposer), ou
   - un `.ffp` FabFilter Pro-Q 3, ou
   - un modèle des **listes déroulantes Casque/écouteurs** (321 modèles
     embarqués, aucune dépendance à l'application) + bouton **Appliquer**.
5. **Éditez directement dans le greffon** :
   - glisser un point de bande = fréquence (X) + gain (Y) ;
   - molette/trackpad sur un point = facteur de qualité Q ;
   - double-clic sur la courbe = créer une bande ;
   - double-clic sur un point = activer/désactiver ;
   - clic droit sur un point = type (cloche, shelf, passe-bas, passe-haut,
     notch) / suppression.
   Chaque édition re-sérialise le preset au format exact de l'application :
   la correction survit à la sauvegarde du projet (état de session) et le
   `.json` reste relisible par l'application.
6. La courbe s'affiche ; le **Préampli** est automatisable dans Live ; le
   bouton bypass de Live utilise le bypass standard du greffon.

## Diagnostic du flux audio

Si « le son ne change pas », l'éditeur tranche en un coup d'œil :

- **VU-mètres In/Out** (coin haut droit) : figés = l'audio ne traverse pas
  le greffon (vérifier le routage de la piste) ; en mouvement = le greffon
  reçoit et traite bien.
- **Bannières orange** : « Aucun preset chargé » (greffon transparent) ou
  « Preset plat » (preset sans effet audible).
- **Version** (coin bas droit) : vérifie le composant installé.
- **Journal** `~/Library/Logs/RoomEq/roomeq-au.log` (miroir de
  roomeq-vst.log sous Windows) : ouverture, auto-chargement,
  initialisation (taux/canaux), premier bloc (preset actif, effet audible),
  fermeture (nombre de blocs traités — 0 bloc = l'audio ne passe jamais).

## Tests et validation

- **Cœur C++ portable** (`core/`, aucune dépendance) : port exact de
  `RoomEq.Core` (biquads RBJ TDFII, chaîne, préampli, écriture JSON,
  banque casques). Les tests natifs (`tests/`) comparent la réponse
  impulsionnelle et la courbe de réponse aux références générées par le
  code C# (`tools/gen_reference`) : **écart nul** (parité bit à bit), sur
  Linux comme sur macOS — plus l'écriture JSON (aller-retour exact), la
  banque (321 modèles, clamps, libellés), le noyau d'édition
  (`edit/preset_editor.cpp` : clamps, chunk round-trip, presets plats) et
  l'auto-chargement par récence : **15 671 vérifications**.
- **CI macOS** (`.github/workflows/build.yml`, job `plugin-au`) : compile le
  bundle universel (arm64 + x86_64, macOS 11+), le signe ad hoc, exécute
  les tests natifs puis `auval` (l'outil officiel de validation d'Apple) et
  joint `RoomEq-AU-<version>-macos.zip` et `.pkg` aux releases.
- Régénérer les références après toute évolution du modèle C# :
  `dotnet run --project natif/roomeq-au/tools/gen_reference`.
- Régénérer la banque casques après évolution de la bibliothèque de
  l'application : `python3 scripts/gen_headphone_bank.py`.

## Limites (v2, documentées spec I-AUD-07)

- Un preset `.ffp` Pro-Q 3 avec pentes de coupe 24/48 dB/oct est importé en
  pente 12 dB/oct (celle des biquads RoomEq) ; les exports RoomEq ne
  produisent que du 12 dB/oct, donc leurs fichiers reviennent exacts.
- Bandes dynamiques Pro-Q 3 : importées comme EQ statique.
- Mono et stéréo uniquement (comme le bus master de l'application).
- Pas de sauvegarde de preset depuis le greffon : l'édition vit en mémoire
  et dans l'état de session du projet DAW (l'application reste l'outil de
  mesure/export).

## Arborescence

| Chemin | Rôle |
|---|---|
| `core/roomeq/` | cœur C++ portable (dsp, preset, json, **banque casques, écriture JSON** — partagé avec le greffon VST, D23) |
| `edit/` | noyau d'édition portable (état de preset éditable, auto-chargement) — testé sous Linux |
| `au/` | greffon AUv2 (RoomEqAu.cpp) + vue Cocoa éditable (RoomEqAuView.mm) + Info.plist |
| `tests/` | tests natifs + données de référence (générées, commitées) |
| `tools/gen_reference/` | générateur C# des références (dotnet run) |
| `thirdparty/AudioUnitSDK/` | SDK Apple AudioUnitSDK 1.4.0 vendangé (Apache-2.0) |
| `CMakeLists.txt` | cœur + couche d'édition + tests (toutes plateformes) ; bundle .component (macOS) |
| `build-macos.sh` | build + signature + auval + installation |
