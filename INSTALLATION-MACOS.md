# Installation de RoomEq sur macOS

Ce document décrit l'installation, l'utilisation du périphérique virtuel
« RoomEq Virtual » (greffon système RoomEqHAL) et les spécificités macOS —
l'équivalent du `INSTALLATION-PILOTE.md` Windows.

## 1. Configuration requise

- macOS 11 (Big Sur) ou plus récent, Apple Silicon (arm64) ou Intel (x64) ;
- Aucun prérequis logiciel : le runtime .NET 8 est embarqué (build auto-contenu) ;
- Pour la **calibration** de pièce : un micro (de mesure type UMIK/ECM8000
  recommandé) — macOS demandera l'autorisation d'accès au premier lancement
  (Réglages Système › Confidentialité et sécurité › Micro).

## 2. Installation (paquet .pkg)

1. Téléchargez le paquet correspondant à votre Mac :
   - `RoomEq-2.0.0-macos-arm64.pkg` — Apple Silicon (M1/M2/M3/M4) ;
   - `RoomEq-2.0.0-macos-x64.pkg` — Intel.
2. Double-cliquez (clic droit › Ouvrir si Gatekeeper se méfie d'un paquet non
   notarisé — voir §5) et suivez l'assistant : il installe
   - `RoomEq.app` dans `/Applications` ;
   - le greffon `RoomEqHAL.driver` dans `/Library/Audio/Plug-Ins/HAL/` ;
   - puis relance `coreaudiod` : **« RoomEq Virtual » apparaît aussitôt** dans
     Réglages Système › Son › Sortie.

En ligne de commande : `sudo installer -pkg RoomEq-2.0.0-macos-*.pkg -target /`.

## 3. Utiliser le périphérique virtuel

1. **Réglages Système › Son › Sortie** : choisissez **« RoomEq Virtual »**.
   (macOS interdit aux applications de changer la sortie par défaut : ce
   réglage est à faire une fois, à la main — RoomEq affiche en permanence
   si c'est déjà fait.)
2. Dans RoomEq, onglet **Mixage**, cliquez **« Router le son vers l'EQ »** :
   le son du système traverse l'égaliseur puis sort sur la sortie physique
   choisie dans la tranche MASTER.
3. Le statut du pont s'affiche en continu dans la barre du Mixage
   (« Périphérique virtuel actif (48 kHz)… »).
4. **Studio** : les DAW macOS (Logic, Live, Cubase…) peuvent aussi sortir
   directement sur « RoomEq Virtual » — même chemin, même égaliseur. (Pas
   d'ASIO sur macOS : CoreAudio est l'interface native des DAW.)
5. **Enregistrement du son système** : n'importe quelle application
   d'enregistrement (QuickTime, OBS…) peut capter l'entrée
   « RoomEq Virtual » — c'est la boucle du son joué.

## 4. Corrections casques / écouteurs

Sur macOS comme sur Windows, l'onglet Égaliseur embarque **255 corrections
prêtes à l'emploi** (toutes gammes, données AutoEq, cible Harman) — la capture
au micro n'étant pas applicable aux casques. Marque + modèle, ou recherche
instantanée, puis « Appliquer la correction casque ».

## 5. Signature et Gatekeeper

Les builds de la CI sont signés **ad-hoc** (identité `-`). C'est suffisant pour
un usage personnel sur macOS ≤ 14 ; **macOS 15 (Sequoia) peut refuser le
chargement par coreaudiod d'un greffon ad-hoc**. Deux voies, exactement comme
la double voie « signature test / attestation » de Windows :

| Voie | Usage | Action |
|---|---|---|
| Ad-hoc (CI) | Développement, tests personnels | Clic droit › Ouvrir pour l'app ; pour le greffon, macOS ≤ 14 ou autorisation en mode développeur |
| **Developer ID** | Distribution | Signer `RoomEqHAL.driver` et `RoomEq.app` avec `codesign --sign "Developer ID Application: …"`, puis **notariser** le .pkg (`notarytool`) ; Gatekeeper et AMFI acceptent alors tout |

Commandes de signature pour la distribution (depuis un Mac avec Xcode) :

```bash
bash installer/macos/build_pkg.sh both                    # produit les .pkg ad-hoc
codesign --force --deep --options runtime --timestamp \
    --sign "Developer ID Application: VOTRE NOM (TEAMID)" \
    macos/RoomEqHAL/RoomEqHAL.driver
# puis re-construire le .pkg et le soumettre :
xcrun notarytool submit RoomEq-2.0.0-macos-arm64.pkg --keychain-profile NOTARY --wait
xcrun stapler staple RoomEq-2.0.0-macos-arm64.pkg
```

## 6. Désinstallation

- **Paquet complet** : le désinstalleur de la section Logiciels (ou
  `sudo pkgutil --forget com.roomeq.RoomEq` après suppression manuelle) ;
- **Greffon seul** :
  ```bash
  sudo rm -rf /Library/Audio/Plug-Ins/HAL/RoomEqHAL.driver
  sudo killall coreaudiod
  ```
- **Application seule** : glisser `RoomEq.app` à la corbeille.

## 7. Dépannage

| Symptôme | Cause probable | Remède |
|---|---|---|
| « RoomEq Virtual » absent de Réglages Système | greffon non installé / refusé (signature) | Réinstallez le .pkg ; §5 pour la signature ; vérifiez `/Library/Audio/Plug-Ins/HAL/` |
| Pas de son malgré le routage | la sortie macOS n'est pas « RoomEq Virtual » | Réglages Système › Son › Sortie |
| « Pont du périphérique virtuel à l'arrêt » | RoomEq n'a pas démarré le pont | Redémarrez RoomEq (le pont démarre automatiquement) ; vérifiez que `/tmp/roomeq.device.bridge.v1.mmf` est créé |
| La calibration ne capte rien | permission micro refusée | Réglages Système › Confidentialité et sécurité › Micro › RoomEq |
| Le son grésille après mise en veille | coreaudiod a perdu le pont | `sudo killall coreaudiod` (recharge le greffon, RoomEq réaligne sa session) |

## 8. Build depuis les sources

```bash
# application (auto-contenue) :
dotnet publish src/RoomEq.App -c Release -r osx-arm64 --self-contained true

# greffon + paquet complet (app + greffon + .pkg, les deux architectures) :
bash installer/macos/build_pkg.sh both
```

La CI GitHub Actions (job `macos`) fait tout cela automatiquement et publie
les paquets en artefacts.
