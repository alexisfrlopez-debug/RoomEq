# RoomEq AU — greffon AudioUnit « RoomEq Correction » (.component)

Composant natif **N-AU-01** (spec `I/audio/I-greffon-au.md`, décision D19) :
applique la correction acoustique RoomEq **dans Ableton Live** (et tout DAW
macOS acceptant les AudioUnits), à partir d'un preset exporté de
l'application.

```
Application RoomEq (mesure, réglage, export)
        │  preset .json (format de l'application) ou .ffp (FabFilter Pro-Q 3)
        ▼
RoomEq.component — RoomEq: Correction (aufx / Rmeq / Rmeq)
   • mêmes biquads RBJ que l'application (parité bit à bit, tests natifs)
   • paramètre « Préampli » automatisable, bypass standard du DAW
   • vue : charger un preset (bouton ou glisser-déposer) + courbe de réponse
```

## Construire (macOS)

Prérequis : Xcode (outils de ligne de commande) et CMake ≥ 3.22.

```bash
bash natif/roomeq-au/build-macos.sh            # compile, teste, signe, valide (auval)
bash natif/roomeq-au/build-macos.sh --install  # + installe pour l'utilisateur
```

Le bundle produit est `build-macos/RoomEq.component`. Sans `--install`,
copiez-le dans `~/Library/Audio/Plug-Ins/Components/` puis retirez
l'éventuel attribut de quarantaine :

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
3. Insérez-le sur une piste ou sur le master, cliquez **Charger un preset…**
   (ou glissez un fichier) et choisissez :
   - un `.json` exporté de RoomEq (onglet Égaliseur › enregistrer le preset ;
     l'application les conserve dans `~/.config/RoomEq/presets/`), ou
   - un `.ffp` FabFilter Pro-Q 3 exporté par RoomEq (ou par Pro-Q 3
    lui-même).
4. La courbe s'affiche ; le **Préampli** est automatisable dans Live ; le
   bouton bypass de Live utilise le bypass standard du greffon.

## Tests et validation

- **Cœur C++ portable** (`core/`, aucune dépendance) : port exact de
  `RoomEq.Core` (biquads RBJ TDFII, chaîne, préampli). Les tests natifs
  (`tests/`) comparent la réponse impulsionnelle et la courbe de réponse aux
  références générées par le code C# (`tools/gen_reference`) : **écart nul**
  (parité bit à bit), sur Linux comme sur macOS.
- **CI macOS** (`.github/workflows/build.yml`, job `plugin-au`) : compile le
  bundle, le signe ad hoc, exécute les tests natifs puis `auval` (l'outil
  officiel de validation d'Apple) et joint `RoomEq-AU-<version>-macos.zip`
  aux releases.
- Régénérer les références après toute évolution du modèle C# :
  `dotnet run --project natif/roomeq-au/tools/gen_reference`.

## Limites (v1, documentées spec I-AUD-07)

- Lecture seule : les bandes ne s'éditent pas dans le greffon (seuls le
  préampli et le bypass sont pilotables) — l'édition se fait dans
  l'application, puis on recharge le preset.
- Un preset `.ffp` Pro-Q 3 avec pentes de coupe 24/48 dB/oct est importé en
  pente 12 dB/oct (celle des biquads RoomEq) ; les exports RoomEq ne
  produisent que du 12 dB/oct, donc leurs fichiers reviennent exacts.
- Bandes dynamiques Pro-Q 3 : importées comme EQ statique.
- Mono et stéréo uniquement (comme le bus master de l'application).

## Arborescence

| Chemin | Rôle |
|---|---|
| `core/roomeq/` | cœur C++ portable (dsp, preset, json) — port de RoomEq.Core |
| `au/` | greffon AUv2 (RoomEqAu.cpp) + vue Cocoa (RoomEqAuView.mm) + Info.plist |
| `tests/` | tests natifs + données de référence (générées, commitées) |
| `tools/gen_reference/` | générateur C# des références (dotnet run) |
| `thirdparty/AudioUnitSDK/` | SDK Apple AudioUnitSDK 1.4.0 vendangé (Apache-2.0) |
| `CMakeLists.txt` | cœur + tests (toutes plateformes) ; bundle .component (macOS) |
| `build-macos.sh` | build + signature + auval + installation |
