# RoomEq — Égaliseur temps réel Windows & macOS avec calibration acoustique automatique

> **Depuis la v2.0, RoomEq est 100 % autonome et multiplateforme** : sur **Windows**, sa propre **carte son virtuelle** (pilote intégré « RoomEq Virtual Audio ») et son propre **pilote ASIO** (« RoomEq ASIO ») pour faire sortir un DAW directement dans l'égaliseur ; sur **macOS**, son propre **périphérique virtuel** (« RoomEq Virtual », greffon système RoomEqHAL) ; et sur les deux systèmes, un **installateur complet** (MSI / .pkg). **Aucune application tierce** (câble audio virtuel, console de mixage externe) n'est requise ni même téléchargée. La **v2.0** ajoute une **bibliothèque intégrée de corrections casques/écouteurs** (toutes gammes, données AutoEq) — la capture au micro n'étant pas applicable à ce type d'écoute. La **v2.1.0** porte la bibliothèque à **321 modèles**, ajoute le **rafraîchissement automatique à chaud** des périphériques (branchement d'un casque détecté sans relancer) et le **preset automatique par périphérique de sortie** : calibration reconnue ou casque reconnu → l'égaliseur s'adapte tout seul, et tout choix manuel est mémorisé pour les sessions suivantes.

RoomEq est une application de bureau qui combine **égaliseur paramétrique**, **table de mixage avec routage du son du système vers l'égaliseur** (périphérique virtuel intégré), **analyseur de fréquences temps réel**, **gestion des pilotes ASIO** (Windows), **calibration acoustique automatique de la pièce** et **corrections prêtes à l'emploi pour 255 casques et écouteurs** : l'application joue une série de signaux de test (bruits et balayage sinusoïdal progressif), capture le micro à la position d'écoute, **croise les résultats de tous les signaux**, analyse la réponse en fréquences et **génère un preset de correction au nombre de bandes, fréquences et largeurs optimisés, appliqué automatiquement à l'égaliseur** — ré-importable à tout moment depuis la courbe affichée, avec une précision au choix. Une bibliothèque intégrée de profils de correction pour 36 micros du marché (toutes technologies) et un éditeur graphique de courbe permettent d'affiner la correction du micro ; un fichier `.cal` REW ou CSV reste importable.

---

## Sommaire

1. [Fonctionnalités](#fonctionnalités)
2. [Installation et lancement](#installation-et-lancement)
3. [Guide d'utilisation](#guide-dutilisation)
4. [Mixage & routage : le son de Windows à travers l'égaliseur](#mixage--routage--le-son-de-windows-à-travers-légaliseur)
5. [Procédure de calibration complète](#procédure-de-calibration-complète)
6. [Profil de correction micro](#profil-de-correction-micro)
7. [ASIO : installation et limites](#asio--installation-et-limites)
8. [Fichiers et dossiers](#fichiers-et-dossiers)
9. [Compiler depuis les sources](#compiler-depuis-les-sources)
10. [Architecture technique](#architecture-technique)
11. [Dépannage et questions fréquentes](#dépannage-et-questions-fréquentes)
12. [Licence et distribution](#licence-et-distribution)

---

## Fonctionnalités

| Module | Détails |
|---|---|
| **Égaliseur paramétrique** | Bandes peaking librement positionnables (fréquence 20 Hz – 20 kHz, gain ±24 dB, Q 0,1 – 20), filtres shelves/passes/notch, courbe composite dessinée en temps réel, poignées déplaçables à la souris (molette = Q), préamplification anti-écrêtage, presets sauvegardables en JSON **et renommables**, **import de la courbe de calibration avec précision au choix** (douce/standard/fine/très fine → nombre de bandes, espacement et Q adaptés, fréquences et gains optimisés automatiquement) |
| **Analyseur de fréquences** | RTA 1/6 d'octave en temps réel, source « Sortie » (signal post-égalisation dérivé du flux de lecture) ou « Micro », taille de FFT réglable (2 048 → 16 384), compensation du profil micro en option |
| **Calibration multi-signaux** | **Tous les signaux disponibles mesurés successivement** — bruit blanc, rose, **brun**, **violet**, puis **balayage sinusoïdal progressif (20 Hz → 20 kHz, déconvolution de Farina : tout le spectre étalonné en continu)** — puis **croisement médian robuste** des résultats (un signal pollué n'influence pas la courbe), dispersion inter-signaux utilisée pour doser prudemment les zones peu fiables, et **placement automatique des filtres sans aucun doublon** : nombre de bandes, fréquences centrales et largeurs (Q) choisis par l'application pour une courbe optimale, zones d'exclusion anti-doublon (mode « bruit blanc seul » conservé) ; bouton **« Réinitialiser la courbe »** pour vider le graphe et repartir de zéro avant un nouveau test ; **journal de diagnostic complet** (micro choisi, formats, niveaux, erreurs) dans `logs/` à côté de l'exe, **erreurs affichées en rouge dans l'onglet**, micros virtuels signalés, **sélecteur de canal mono** (canal 1/2 seul pour les micros multi-canaux dont une voie est muette ou parasite), capture démarrée avant la lecture (balayage jamais rogné), format de capture adaptatif, un signal en échec n'annule plus la mesure entière |
| **Correction micro** | Import `.cal` REW / CSV (`;` ou `,`, décimales `.` ou `,`), **bibliothèque intégrée de 36 micros du marché** (mesure, studio, scène — électret, condensateur vrai, RF, dynamique, ruban, USB) avec recherche, **éditeur graphique de courbe** (points ajoutés/déplacés/supprimés à la souris, champs numériques, lissage), **création de profils spécialisés**, renommage, profil appliqué avant l'analyse de la pièce |
| **Corrections casques (v2.0 → v2.1)** | **Bibliothèque intégrée de 321 modèles** (52 marques, toutes gammes : petit prix → haut de gamme ; toutes familles : circum-aural, intra-aural, écouteurs classiques, casques gaming, sans fil ANC — Sennheiser, Beyerdynamic, AKG, Sony, Audio-Technica, Bose, Apple, HiFiMAN, Focal, Audeze, Moondrop, Truthear, HyperX, SteelSeries…) : sélection par marque/modèle **ou recherche instantanée** (« hd 650 », « xm5 »…), application en un clic (préampli + 10 bandes paramétriques), source de mesure affichée par modèle ; données dérivées du projet **AutoEq** (MIT, cible Harman) — la capture au micro n'étant pas applicable aux casques |
| **Preset automatique par sortie (v2.1)** | L'égaliseur s'adapte au **périphérique réellement écouté** (sortie master de l'onglet Mixage) : preset de **calibration** si une mesure a été faite sur cette sortie, sinon correction **usine** si le casque/écouteur est reconnu dans son nom Windows (« Casque (JBL Tune Flex 2 Stereo) »…). Un choix manuel par sortie (sélecteur « Preset de la sortie » ou bouton « Lier ce preset à la sortie ») est **mémorisé d'une session à l'autre** (%APPDATA%\RoomEq\devicepresets.json) |
| **Périphériques à chaud (v2.1)** | Brancher/débrancher un casque Bluetooth, une carte USB ou changer la sortie par défaut **met à jour toutes les listes automatiquement** (surveillance système WASAPI, anti-rebond 1 s) — plus besoin de relancer ; les boutons « Rafraîchir » / « ⟳ » restent disponibles |
| **ASIO** | **Pilote ASIO intégré « RoomEq ASIO » (v2.0)** : votre DAW (REAPER, Cubase, FL Studio, Ableton…) choisit « RoomEq ASIO » comme sortie, sa lecture alimente la piste **« DAW (ASIO) »** du mixeur puis traverse l'égaliseur vers la vraie carte son (48 kHz stéréo, buffers 128–2048) ; énumération des autres pilotes ASIO installés, lecture exclusive via ASIO (taux et canaux négociés automatiquement), informations de capacité (canaux, buffer, latence, taux supportés), ouverture du panneau de contrôle du pilote |
| **Mixage & routage système** | Onglet « Mixage & routage » : table de mixage complète dont les pistes correspondent à **toutes les entrées et toutes les sorties de Windows** (plus pont DAW, lecteur de fichiers et générateur internes), **égaliseur propre à chaque piste**, fader/pan/mute/solo et VU-mètres stéréo à crêtes, bus master avec l'EQ principal synchronisé en temps réel, **carte son virtuelle RoomEq intégrée** (pilote `roomeqvad` livré et installé avec l'application — paire « RoomEq Input / RoomEq Output », aucun téléchargement) ou **boucle WASAPI** de secours sans pilote, avec **installation du pilote en un clic** et **bascule automatique des périphériques par défaut de Windows** (sortie et entrée, restaurée à l'arrêt) pour que **tout le son de Windows passe par l'égaliseur** ; **volume de la carte physique routée** (le volume Windows de la carte virtuelle est sans effet) piloté par le **curseur Volume de la barre supérieure (0–100 %)**, par raccourcis clavier **Ctrl+Alt+↑ / ↓ (±5 %)** et **Ctrl+Alt+M (sourdine)** avec affichage OSD, ou depuis la tranche MASTER — **les deux curseurs de volume (barre du haut et tranche MASTER) sont synchronisés en permanence**, quel que soit celui que l'on déplace et dans quel ordre, y compris pour un réglage fait hors de l'application (mixeur Windows, touches du casque) ; **tranches défilables verticalement** (chaque tranche entièrement visible en descendant la page) |
| **Lecteur intégré** | Lecture de fichiers wav / mp3 / flac / aiff / wma / m4a avec EQ en temps réel, **menu déroulant des signaux de test** : bruits blanc / rose / **brun** / **violet**, **sinus de 50 Hz à 15 kHz (10 fréquences utiles)**, **balayages logarithmiques** (20 Hz → 20 kHz et médium 200 Hz → 2 kHz), **+ signaux supplémentaires téléchargeables depuis le dépôt officiel du projet** (impulsions Dirac, bruit gris d'égale sonie, multi-tones SMPTE, balayage lent 30 s — vérifiés par SHA-256, stockés dans `%APPDATA%\RoomEq\signaux`), boucle, volume, bypass |
| **Aide contextuelle « ? »** | Chaque fonctionnalité des 6 onglets (et de la barre du haut) est accompagnée d'un bouton **« ? »** discret : infobulle complète au survol, boîte de dialogue détaillée au clic |
| **Défilement de l'interface** | **Défilement horizontal** de la barre supérieure (le **menu des signaux de test** et le bouton **Télécharger d'autres…** restent accessibles sur fenêtre étroite), de la barre de contrôle de l'Analyseur et de la barre de routage du Mixage ; barre supérieure allégée (tout tient sans défilement sur écran 1920 px) ; tranches du Mixage défilables verticalement **et** horizontalement (1.8.0) |
| **Versions test / public** | Un même binaire, deux noms : **`RoomEq.exe` (public, par défaut — aucun journal écrit, aucun envoi)** et `RoomEq-test.exe` (journalisation complète + télémétrie Drive) ; version visible dans la barre de titre ; au lancement de la version test, une boîte de dialogue propose de lancer la version publique à la place ; le mode test peut aussi être forcé par la variable d'environnement `ROOMEQ_TEST=1` (développement) |
| **Télémétrie de test** | Version test uniquement : le journal de diagnostic part automatiquement vers le dossier Drive « journaux-app » du développement (fermeture / toutes les 5 min / bouton « Envoyer maintenant »), statut affiché dans l'onglet Calibration ; piloté par `telemetrie.json` à côté des exécutables — ignoré par la version publique |

## Installation et lancement

### Distribution « exécutables » (recommandée si Windows bloque le MSI)

Trois fichiers, zéro manipulation PowerShell, zéro `msiexec` :

1. **`RoomEqDriverSetup-2.2.1.exe`** — la carte son virtuelle + le pilote ASIO, pilote embarqué à l'intérieur du fichier. **v2.2.0 : interface graphique complète** — double-clic → UAC → fenêtre avec état de la machine, journal, et boutons **Installer le pilote / Diagnostic complet / Revenir au mode normal / Désinstaller** ; zéro ligne de commande. **v2.2.1 : correctif critique** — l'étape 5/6 (création du périphérique) échouait avec l'erreur `0xE0000205` sur toutes les machines (INF de style legacy) : corrigé, les cartes « RoomEq Input / Output » apparaissent maintenant. Le mode test reste **proposé explicitement — jamais activé en silence** (boîte de dialogue, défaut : non). Options console (support) : `--status` (état complet + **verdict**), `--uninstall`, `--mode-normal`, `--accept-testmode` / `--no-testmode`.
2. **`RoomEq-2.2.1-win-x64.exe`** — l'application entière en **un seul fichier portable** (~45 Mo) : aucune installation, aucun UAC, lancez-la depuis le Bureau ou une clé USB.
3. **`RoomEqAppSetup-2.2.1.exe`** — la même application en **version installable** (le portable **et le setup pilote** sont embarqués dedans) : installation **sans UAC** dans `%LOCALAPPDATA%\Programs\RoomEq`, raccourcis Menu Démarrer/Bureau, **lancement automatique au démarrage de Windows** (proposé, désactivable), entrée « Applications & fonctionnalités » avec désinstalleur — `RoomEqDriverSetup.exe` est déposé à côté de `RoomEq.exe` pour que le bouton « Installer le pilote » de l'onglet Mixage ouvre **sa fenêtre graphique**. Options : `--status`, `--uninstall`, `--no-autostart`, `--no-desktop`.

> Portable ou installée : choisissez — les réglages et mesures (`%APPDATA%\RoomEq`) sont partagés entre les deux et conservés à la désinstallation.

> Premier lancement après téléchargement : clic droit → Propriétés → « Débloquer » (SmartScreen, voir `TROUBLESHOOT-INSTALLATION.md`). Le **Secure Boot** doit être désactivé tant que le pilote est signé-test (le setup le détecte et l'explique).

### Installateur MSI (voie historique)

1. Téléchargez `RoomEq-2.0.0-win-x64.msi` (GitHub → Actions/Releases) et double-cliquez.
2. Acceptez l'invite administrateur — **c'est tout**. L'installateur déploie l'application dans `Program Files\RoomEq` (runtime .NET 8 embarqué, aucun logiciel tiers), puis installe la **carte son virtuelle RoomEq** de bout en bout : certificat de signature de test importé, mode test de Windows activé (redémarrage proposé si nécessaire), pilote `roomeqvad` ajouté au magasin Windows, périphérique virtuel créé, et **pilote ASIO « RoomEq ASIO »** enregistré pour vos DAW.
3. Après l'éventuel redémarrage, les périphériques **« RoomEq Input » / « RoomEq Output »** apparaissent dans le panneau son (un filigrane « Mode de test » en bas à droite de l'écran est normal).

La désinstallation (Paramètres → Applications) retire proprement périphérique, pilote, certificat et application. Le pilote peut aussi être retiré/reposé depuis l'onglet Mixage (« Supprimer le pilote » / « Installer le pilote RoomEq… »).

> **Signature du pilote** : Windows n'accepte que des pilotes signés. Tant que le projet n'a pas de certificat EV (attestation Microsoft — voir `INSTALLATION-PILOTE.md`), les builds sont signés-test et le MSI active le mode test automatiquement ; si le **Secure Boot** est actif dans le BIOS/UEFI, ce mode ne peut pas être activé et l'installateur s'arrête avec un message explicatif (voyez `INSTALLATION-PILOTE.md`). L'application elle-même fonctionne sans pilote via la boucle WASAPI.

### Build portable (alternative sans installation)

1. Décompressez le zip choisi où vous voulez (le dossier doit rester inscriptible : les presets vont dans `%APPDATA%`, le journal de calibration dans `logs\` à côté de l'exe) :
   - `RoomEq-2.0.0-win-x64-public.zip` → contient **`RoomEq.exe` seul** (public, silencieux) ;
   - `RoomEq-2.0.0-win-x64-test.zip` → contient **`RoomEq.exe` ET `RoomEq-test.exe`** + `telemetrie.json` + le paquet pilote (`driver\`).
2. Double-cliquez sur **`RoomEq.exe`** (public) ou **`RoomEq-test.exe`** (test).
3. Aucun prérequis : le runtime .NET 8 est embarqué (build auto-contenu).

> **Les deux versions, même binaire** : depuis la v2.0, le silence est la valeur par défaut — le nom déduit la version. `RoomEq.exe` ne journalise rien (aucun dossier `logs\`, ni fichier ni mémoire) et n'envoie rien, même si `telemetrie.json` est présent. `RoomEq-test.exe` (ou la variable `ROOMEQ_TEST=1`) active la journalisation complète et la télémétrie Drive (fermeture + toutes les 5 minutes — uniquement du texte de journal, jamais de contenu audio). Au lancement de la version test, une boîte de dialogue propose de lancer la version publique à la place.

Configuration minimale : Windows 10 (1809+) ou 11, x64. Un micro est nécessaire pour la calibration (un micro de mesure type UMIK/ECM8000 est recommandé pour la précision).

### macOS (v2.0)

1. Téléchargez `RoomEq-2.0.0-macos-arm64.pkg` (Apple Silicon) ou `RoomEq-2.0.0-macos-x64.pkg` (Intel) et double-cliquez.
2. Acceptez l'invite administrateur : l'installateur déploie `RoomEq.app` dans `/Applications`, installe le **greffon RoomEqHAL** dans `/Library/Audio/Plug-Ins/HAL` et relance `coreaudiod` — le périphérique **« RoomEq Virtual »** apparaît immédiatement dans Réglages Système › Son.
3. Choisissez « RoomEq Virtual » comme sortie macOS, puis dans RoomEq cliquez **« Router le son vers l'EQ »** : tout le son du système traverse l'égaliseur.

Détails, signature (Developer ID) et désinstallation : **`INSTALLATION-MACOS.md`**.

### ASIO : pilote intégré + vos autres pilotes

- **« RoomEq ASIO » est fourni avec l'application** (installé par le MSI ou enregistré par le zip de test) : dans votre DAW, choisissez-le comme périphérique de sortie, puis dans RoomEq ajoutez la piste **« DAW (ASIO) »** dans l'onglet Mixage — la lecture du DAW traverse l'égaliseur puis sort sur la carte physique choisie dans la tranche MASTER (48 kHz stéréo ; lisez le statut du pont dans la barre du Mixage : « DAW connecté… »).
- Les **autres** pilotes ASIO restent utilisables en sortie d'écoute (RME, Focusrite, MOTU… ou [ASIO4ALL](https://www.asio4all.org/)) — l'ASIO n'est pas requis pour la calibration, le WASAPI partagé suffit.

## Guide d'utilisation

### Barre supérieure

- **ComboBox de sortie** : liste les périphériques WASAPI actifs et les pilotes ASIO installés (préfixés « ASIO · »). Le changement de sortie redémarre proprement la lecture en cours.
- **Ouvrir un fichier…** : lecture d'un fichier audio à travers l'égaliseur.
- **Menu des signaux de test** : choisissez un signal (bruits blanc/rose/brun/violet, sinus de 50 Hz à 15 kHz, balayages, fichiers téléchargés) puis **« Jouer »** pour l'écouter sur la sortie de lecture ; **« Télécharger d'autres… »** ouvre le catalogue officiel (impulsions, bruit gris, multi-tones, balayage lent) et installe les fichiers choisis localement.
- **Volume** : gain maître du lecteur interne appliqué après l'EQ (100 % = 0 dB, 150 % = +3,5 dB) **et** volume Windows de la carte physique routée de 0 à 100 % — le son routé ne passe pas par le lecteur interne, c'est ce qui rend le curseur audible en usage routé. **1.9.1** : le curseur du Mixage et les raccourcis clavier reçoivent désormais la barre du haut en retour — les deux curseurs restent identiques quel que soit l'ordre de manipulation (une seule source de vérité : la carte ; `VolumeSyncPolicy` teste les règles d'écriture/recadrage).
- **Bypass EQ** : compare instantanément avant/après correction.

### Onglet Égaliseur

- **Glissez une poignée** dans le graphe : horizontal = fréquence, vertical = gain.
- **Molette au-dessus d'une poignée** : élargit/resserre la bande (facteur Q).
- Le panneau de droite permet l'édition numérique précise (type de filtre compris) et la gestion des presets (charger / enregistrer / **renommer** / supprimer).
- **Préamplification** : baissez-la si la correction comporte des boosts ; RoomEq la calcule automatiquement (-boost max) après une calibration.

### Onglet Analyseur

- **Source « Sortie »** : spectre du signal réellement envoyé à la carte, **après** l'égaliseur (dérivation du flux de lecture). Utile pour visualiser l'effet du preset.
- **Source « Micro »** : appuyez sur « Démarrer le micro » pour analyser l'entrée sélectionnée (mesure du bruit ambiant, vérification du positionnement…).
- **Compenser le profil micro** : applique la courbe du profil importé (onglet Micro) à l'affichage.

## Mixage & routage : le son de Windows à travers l'égaliseur

L'onglet **Mixage & routage** transforme RoomEq en **carte son virtuelle + table de mixage** : le son de toutes les applications (Spotify, navigateur, jeux…) transite par l'égaliseur avant d'atteindre vos enceintes.

### Principe : la carte son virtuelle RoomEq (intégrée)

Windows n'autorise pas une application à se déclarer seule comme carte son : il faut un **pilote audio virtuel**. RoomEq embarque **le sien** — plus aucun logiciel tiers :

| Protocole | Origine | Installation | Périphériques |
|---|---|---|---|
| **Carte virtuelle RoomEq** (recommandé) | **Pilote intégré** `roomeqvad` (livré avec l'application) | **Un clic** : « Installer le pilote RoomEq… » (paquet local, aucun téléchargement) ou automatiquement par le MSI | « RoomEq Input (RoomEq Virtual Audio) » / « RoomEq Output (RoomEq Virtual Audio) » |
| **Boucle WASAPI** | Intégré à Windows | Aucune (sans pilote) | Boucle directe de n'importe quelle sortie |

La chaîne est **automatique de bout en bout** :

1. **Installez le pilote en un clic** : **« Installer le pilote RoomEq… »** lance l'installation du paquet livré avec l'application (`pnputil`, invite administrateur, **rien n'est téléchargé**), puis RoomEq attend l'apparition des périphériques « RoomEq Input / RoomEq Output ». Le bouton « Supprimer le pilote » fait l'inverse (maintenance).
2. **Bascule automatique de Windows** : dès que les périphériques sont détectés (option « **Router dès l'installation** », active par défaut), la **sortie par défaut** de Windows passe sur « RoomEq Input », la piste « RoomEq Output — Windows » est ajoutée à la console et le routage démarre : le son système traverse l'égaliseur et ressort sur la **sortie physique choisie dans la tranche MASTER**.
3. **Option « Entrée par défaut → carte virtuelle »** (active par défaut) : pendant le routage, l'**entrée par défaut** de Windows devient « RoomEq Output » — les autres applications voient un « micro virtuel » alimenté par le son système (pratique pour Discord, l'enregistrement, le streaming).
4. **Restauration automatique** : à l'arrêt du routage, vos périphériques par défaut d'origine (sortie ET entrée) sont rétablis — ils sont mémorisés dans `mixer.json`, donc restaurés même après fermeture de l'application.
5. Si la bascule automatique est refusée par votre configuration Windows, le bouton « **Définir par défaut** » (API PolicyConfig, trois variantes tentées en cascade) ou le panneau son permettent de le faire manuellement.

> **Signature** : Windows 10/11 64 bits exige des pilotes signés. Sans certificat EV, le pilote est livré signé-test — la procédure d'activation (mode test) et la voie officielle (attestation Microsoft) sont détaillées dans **`INSTALLATION-PILOTE.md`**.

### Studio : le DAW dans l'égaliseur (pilote ASIO intégré)

Dans votre DAW, choisissez le périphérique **« RoomEq ASIO »** (48 kHz). Dès la lecture, l'onglet Mixage affiche « **DAW connecté (48 kHz)** » : ajoutez la piste **« DAW (ASIO) »** (bouton « + Ajouter une piste ») — elle porte la lecture du DAW, traverse son propre EQ de piste puis l'EQ principal, et sort sur la tranche MASTER (la sortie peut être une carte WASAPI **ou** un pilote ASIO pour une latence minimale). Le statut du pont (connexion, attente, trames écartées) est visible en permanence dans la barre du Mixage ; le pilote ASIO fonctionne même si RoomEq est fermé (il se reconnecte au redémarrage de l'application).

### La table de mixage

La console reprend les codes des tables analogiques, avec des **tranches horizontalement défilantes** :

| Élément | Détails |
|---|---|
| **Pistes dynamiques** | « + Ajouter une piste » liste **toutes les entrées** de Windows (micros, lignes, RoomEq Output…), **toutes les sorties en mode boucle** (loopback : tout ce que Windows joue sur cette sortie), la piste **DAW (ASIO)** (lecture du DAW via le pilote ASIO intégré) et les sources internes (lecteur de fichiers, générateur de test). Les pistes correspondent donc à l'intégralité des sources disponibles |
| **Égaliseur par piste** | Chaque tranche possède son **propre EQ paramétrique** (bouton EQ → éditeur à droite : bandes peaking/shelf/pass/notch, courbe de réponse, presets rapides « Coupe-bas 80 Hz / Présence 3 kHz / Aigus adoucis / Aucun EQ ») — indépendant de l'EQ principal, qui reste appliqué au bus master |
| **Fader & pan** | Fader 0 → +3,5 dB de gain utile (0 dB au centre), panoramique à puissance constante |
| **Mute / Solo** | Règle standard : si au moins une piste est en solo, seules les solos passent |
| **VU-mètres** | Stéréo, à crêtes tenues, post-fader ; VU master dédié |
| **Tranche MASTER** | Choix de la sortie (toutes les sorties WASAPI + pilotes ASIO), fader master, mute, **curseur « Vol. carte physique »** (volume système du périphérique de sortie réel : curseur, boutons − / + / M, ou raccourcis Ctrl+Alt+↑ / ↓ / M — **synchronisé avec le curseur Volume de la barre du haut**, quel que soit l'ordre de manipulation) ; l'EQ principal (onglet Égaliseur) est appliqué au bus master et suit vos éditions en temps réel |
| **Sources internes** | Tranche Lecteur (charge un fichier audio dans la console) et tranche Générateur (bruit blanc / rose / sinus 1 kHz) pour les tests |

Au premier lancement, des pistes par défaut sont proposées : **son Windows** (via la carte virtuelle si présente, sinon en boucle sur la sortie par défaut), **lecteur**, **micro** et **générateur**. La configuration complète (pistes, faders, EQ par piste, sortie master, auto-démarrage) est conservée dans `%APPDATA%\RoomEq\mixer.json`.

### Mode boucle (sans pilote)

Sans carte virtuelle, une piste peut capter **en boucle** la sortie d'un périphérique de rendu. Deux précautions :

- choisissez comme **sortie master un périphérique différent** de celui capté — RoomEq **refuse de démarrer** un montage qui créerait une boucle (larsen) ;
- le son original continue de sortir du périphérique capté : pour n'entendre que le signal traité, coupez physiquement le volume de celui-ci ou utilisez un écran HDMI sans écoute.

La carte virtuelle RoomEq (pilote intégré) reste la méthode recommandée : « RoomEq Input » n'a pas de sortie physique, le son ne peut être entendu **qu'après** l'égaliseur.

### Latence et qualité

Le routage fonctionne en **WASAPI partagé** (les autres applications continuent de sonner) avec un tampon de 50 ms côté master ; la latence totale avec la carte virtuelle RoomEq est d'environ 70–90 ms — imperceptible à l'écoute musicale, à connaître pour le doublage labial sur vidéo. Les rééchantillonnages (piste → taux master) utilisent le resampleur WDL ; le traitement EQ est identique au moteur principal (biquads RBJ, échange atomique des filtres).

## Procédure de calibration complète

1. **Préparez la pièce** : fermez fenêtres et portes, coupez ventilation/clim, gardez le silence pendant la mesure (la moyenne de Welch atténue mais n'élimine pas les bruits impulsifs : claquements, clavier, voix).
2. **Micro** : placez-le à la **position d'écoute**, sur pied, à hauteur d'oreille. Pour un micro de mesure à cosinus, orientez-le **vers le plafond** (omni) ou vers les enceintes selon la convention du fabricant. Si vous disposez du fichier de calibration du micro, importez-le d'abord (onglet Micro) — ou choisissez le modèle dans la bibliothèque intégrée.
3. **Niveau** : dans l'onglet Calibration, choisissez le micro — **votre micro physique** (les cartes virtuelles type « RoomEq Output » sont signalées « ne capte pas la pièce » : les choisir fait échouer la mesure) — et réglez le « Niveau du bruit de test » (0,20 par défaut ≈ -14 dBFS). Le volume maître de la barre du haut s'ajoute : visez un niveau d'écoute normal, sans saturation des enceintes.
4. **Signaux** : laissez cochée la **mesure multi-signaux** (recommandée) et les cinq signaux sélectionnés. Chaque signal est mesuré tour à tour : **bruit blanc, rose, brun, violet**, puis le **balayage sinusoïdal progressif** (montée continue de 20 Hz à 20 kHz, durée réglable 6–20 s). La durée par bruit est réglable (8–20 s, 12 s par défaut) ; le plan de mesure et la durée totale sont affichés. Décochez des signaux pour aller plus vite, ou décochez « Mesure multi-signaux » pour retrouver le mode « bruit blanc seul ».
5. **Cible** : « Plate (neutre) » corrige la pièce vers une réponse droite ; « Plate + shelf de graves » conserve un léger renfort d'assise ; « Studio » applique une pente descendante de -1 dB/octave à partir de 1 kHz.
6. **Limites de sécurité** : boost max 6 dB (protège les haut-parleurs et la marge dynamique), coupe max 12 dB (une coupe est toujours préférable à un boost). Ces bornes sont réglables.
7. **Lancez la calibration** : chaque segment est mesuré (spectre live du micro pendant l'opération), puis **toutes les mesures sont croisées** : chaque signal donne une estimation indépendante de la réponse de la pièce (après retrait de sa pente spectrale théorique et alignement), la **médiane** fournit la courbe robuste et la **dispersion inter-signaux** signale les bandes peu fiables (corrections dosées prudemment là où les signaux divergent).
8. **Placement automatique des filtres** : le nombre de bandes, leurs fréquences centrales et leurs largeurs (Q) sont choisis automatiquement par un algorithme glouton qui annule le plus grand écart résiduel à chaque itération avec la vraie réponse des biquads — la correction s'arrête quand la courbe prédite touche la cible, avec un nombre minimal de filtres. **Aucun doublon de bandes** : chaque filtre réserve une zone d'exclusion (déduite de sa largeur de bande, plancher 1/12 d'octave) interdisant qu'un second filtre soit créé au même endroit ; là où une anomalie dépasse le boost maximal, les filtres sont répartis autour d'elle au lieu d'être empilés. Les gains restent bornés (boost/coupe max) et le préampli de sécurité est calculé.
9. **Résultat** : le graphe montre les **courbes individuelles de chaque signal** (traits fins grisés), la **mesure croisée** (cyan), la **cible** (orange pointillé) et la **prédiction corrigée** (vert). Le preset est appliqué immédiatement à l'égaliseur ; vérifiez à l'écoute avec le bouton Bypass.
10. **Réinitialisez si besoin** : le bouton « **Réinitialiser la courbe** » vide le graphe et efface le résultat avant de relancer un test (une mesure en cours est annulée) — pratique pour déplacer le micro et repartir de zéro.
11. **Importez la courbe dans l'Égaliseur** : dans l'onglet Égaliseur, le panneau « **Import calibration** » régénère toutes les bandes à partir de la courbe affichée dans Calibration (mesure croisée, profil micro déjà retiré, alignée sur la cible). Choisissez la **précision** : *Douce* (8 bandes max, larges et prudentes), *Standard* (14 bandes, équilibre), *Fine* (20 bandes, niveau calibration) ou *Très fine* (30 bandes fines, résonances étroites) — la précision détermine le nombre de bandes, leur espacement et leurs largeurs (Q), tandis que les fréquences et les gains restent optimisés automatiquement, toujours sans doublon. Pratique pour comparer plusieurs niveaux de correction sans refaire la mesure.
12. **Itérez** : déplacez légèrement le micro (±30 cm) et refaites une mesure ; les corrections stables d'une mesure à l'autre sont fiables, celles qui bougent relèvent du hasard de la pièce (modes propres) et doivent rester modérées.

**Pourquoi plusieurs signaux ?** Chaque bruit a son spectre propre : le blanc excite uniformément toutes les bandes, le rose suit l'audition, le brun concentre l'énergie dans les graves (modes de pièce mesurés finement), le violet dans les aigus (où les bruits s'affaiblissent), et le **balayage sinusoïdal progressif** étalonne tout le spectre en continu : la fréquence montant lentement de 20 Hz à 20 kHz, chaque fréquence reçoit une excitation dédiée et la **déconvolution de Farina** (division fréquentielle par la référence régénérée, puis fenêtrage de la réponse impulsionnelle) en extrait une mesure narrowband insensible au bruit de fond — aucune fréquence n'est sautée, contrairement à des tons discrets. Le croisement médian élimine les erreurs spécifiques à un signal (résonance excitée, bruit ambiant pendant un segment…) — la courbe finale est plus fidèle que n'importe quelle mesure seule.

**Bornes de validité** : la correction n'agit que dans 40 Hz – 16 kHz (réglable). En dessous, la réponse dépend trop du mode de la pièce et du positionnement ; au-dessus, les micros low-cost deviennent directs.

## Profil de correction micro

L'onglet Micro offre trois chemins complémentaires :

- **Bibliothèque intégrée** : 36 micros du marché couvrant toutes les technologies — mesure (miniDSP UMIK-1/2, Dayton EMM-6/iMM-6, Behringer ECM8000, Superlux ECM999, the t.bone EM 700, Audix TM1, dbx RTA-M, Sonarworks XREF 20, IK ARC, iSEMcon EMX-7150, Earthworks M23/M30/QTC40, DPA 4006A), studio (Neumann TLM 103/KM 184, AKG C414 XLII, AT2020/AT4050, Røde NT1-A/NT5, Lewitt LCT 440, sE7, Sennheiser MKH 8040/416), scène et dynamiques (Shure SM57/SM58/SM7B, Sennheiser e945/MD 421, EV RE20), **rubans passifs** (Royer R-121, beyerdynamic M 160) et un profil plat de départ. Recherche par marque/modèle/technologie ; « Importer ce profil » le copie dans vos profils.
- **Import de fichier** : `.cal` REW (lignes `"fréquence,gain"`, en-têtes ignorés, colonnes supplémentaires tolérées), CSV point-virgule (français), CSV virgule, TXT à espaces ; décimales `.` ou `,`. C'est la source la plus précise : chaque exemplaire de micro a sa propre courbe.
- **Éditeur graphique** : la courbe du profil sélectionné est **directement modifiable** — glissez un point (fréquence + gain), double-cliquez pour en ajouter, clic droit pour en supprimer ; champs numériques pour le point sélectionné, lissage en un clic, création de **profils spécialisés** vierges (plats), renommage, enregistrement. Les courbes de la bibliothèque sont des **typicalités constructeur** : importez le modèle puis affinez-le avec l'éditeur ou votre fichier `.cal` individuel.

Le profil est **soustrait** du spectre capturé avant conception de la correction : si le micro monte de +2 dB à 8 kHz, RoomEq ne va pas « corriger » une enceinte parfaite. Il sert aussi à l'analyseur (case « Compenser le profil micro ») et à la calibration multi-signaux. Profils sauvegardés dans `%APPDATA%\RoomEq\micprofiles`.

## ASIO : installation et limites

- Onglet **ASIO** : liste des pilotes, canaux d'entrée/sortie, taille de buffer, latence, taux d'échantillonnage supportés, bouton **panneau de contrôle** du pilote (réglages constructeur).
- En sortie ASIO, RoomEq joue **en exclusif** : le mixeur Windows est contourné (latence minimale, bit-perfect), les autres applications ne sortent plus par cette interface pendant la lecture. Le taux d'échantillonnage est négocié automatiquement (48 kHz si le pilote l'accepte) et le flux est rééchantillonné vers le taux du pilote si besoin.
- La capture micro reste en WASAPI même en lecture ASIO (les pilotes ASIO consument réservation exclusive ; un micro WASAPI/USB séparé est le cas d'usage normal).
- Pour égaliser **toutes les applications** (Spotify, navigateur…) : utilisez l'onglet **Mixage & routage** (carte son virtuelle RoomEq intégrée + table de mixage). Pour un **DAW**, sortez sur « RoomEq ASIO » et ajoutez la piste « DAW (ASIO) ».

## Fichiers et dossiers

| Emplacement | Contenu |
|---|---|
| `%APPDATA%\RoomEq\presets\*.json` | Presets d'égaliseur (bandes, préampli) |
| `%APPDATA%\RoomEq\micprofiles\*.json` | Profils de correction micro importés |
| `%APPDATA%\RoomEq\mixer.json` | Configuration de la table de mixage (pistes, faders, EQ par piste, sortie master, auto-démarrage, protocole, options de routage, périphériques par défaut mémorisés) |
| `%APPDATA%\RoomEq\settings.json` | (réservé) réglages persistants |
| `logs\roomeq-AAAA-MM-JJ.log` (à côté de l'exe) | **Journal de diagnostic** : sessions, micro sélectionné, formats de capture, niveaux de chaque segment, analyse du balayage, erreurs complètes avec pile — le chemin est affiché en bas de l'onglet Calibration ; à joindre en cas de problème |

## Compiler depuis les sources

Prérequis : [SDK .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0).

```bat
dotnet build RoomEq.sln -c Release
dotnet test  tests\RoomEq.Core.Tests
dotnet publish src\RoomEq.App -c Release -r win-x64 --self-contained true -p:PublishDir=..\..\publish\win-x64
```

L'exécutable se trouve dans `publish\win-x64\RoomEq.App.exe` : copiez-le sous les noms `RoomEq.exe` (public, silencieux) et/ou `RoomEq-test.exe` (test, journalisé).

### Composants natifs (pilote WDM, ASIO, installateur)

Ces composants exigent Windows ; la **CI GitHub Actions les compile automatiquement** (artefacts des workflows) :

| Composant | Où | Comment compiler localement |
|---|---|---|
| Carte virtuelle `roomeqvad.sys` | `driver/roomeqvad/` | `powershell -File driver\roomeqvad\build\build_driver.ps1` (VS2022 + le WDK arrive par NuGet, ~500 Mo au premier build) puis `-TestSign` pour signer-test |
| Pilote ASIO `RoomEqAsio64.dll` | `asio/asiodll/` | `msbuild asio\asiodll\RoomEqAsio.vcxproj /p:Configuration=Release /p:Platform=x64` (MSVC seul) |
| Noyau du pont ASIO (portable) | `asio/bridge_core/` | `make -C asio/bridge_core test` (g++/Linux inclus) |
| Installateur MSI | `installer/` | `powershell -File installer\build_msi.ps1` (SDK .NET 8 + `dotnet tool install --global wix --version 6.0.2`) |

## Architecture technique

```
RoomEq.sln
├── driver/roomeqvad/     Pilote WDM de la carte son virtuelle (WaveRT/PortCls, dérivé
│                         de l'exemple SYSVAD du WDK, MIT) : un endpoint de rendu
│                         « RoomEq Input », un endpoint de capture « RoomEq Output »,
│                         pont interne bridge.cpp (render → capture)
├── asio/                 Pont ASIO RoomEq
│   ├── bridge_core/      Protocole partagé + anneau SPSC + PLL (C portable, testé g++)
│   └── asiodll/          RoomEqAsio64.dll (COM IASIO, mémoire partagée avec l'app)
├── installer/            MSI WiX (app + pilote + ASIO, actions personnalisées pnputil)
├── .github/workflows/    CI : tests → pilote (WDK NuGet) + signature test → MSI
├── src/
│   ├── RoomEq.Core/        Noyau DSP, sans dépendance audio ni UI
│   │   ├── Dsp/            BiquadFilter (RBJ), FFT radix-2 (directe + inverse), Welch,
│   │   │                   bandes d'octave, lissage fractionnaire, générateurs de bruits
│   │   │                   et de balayage logarithmique (LogSweepGenerator, phases exactes),
│   │   ├── Eq/             EqBand, EqPreset (JSON), EqChain temps réel (échange
│   │   │                   atomique des filtres, aucun lock sur le thread audio)
│   │   ├── Calibration/    TargetCurve (plate / shelf / pente), MicProfile (parseur
│   │   │                   tolérant .cal/CSV), CorrectionDesigner (1/3 oct → peaking
│   │   │                   ou placement automatique via AutoBandPlanner ; ré-import
│   │   │                   de la courbe de calibration par niveau de précision),
│   │   │                   CorrectionPrecision (douce → très fine : bandes, espacement,
│   │   │                   Q), TestSignalCatalog (blanc/rose/brun/violet/balayage),
│   │   │                   MultiSignalAnalysis (croisement médian, dispersion),
│   │   │                   AutoBandPlanner (bandes/fréquences/Q optimaux, anti-doublons
│   │   │                   par zones d'exclusion), SweepAnalyzer (déconvolution de
│   │   │                   Farina du balayage → réponse impulsionnelle → bandes 1/6 oct),
│   │   │                   MicProfileLibrary (36 micros embarqués, recherche)
│   │   └── Io/             PresetStore, MicProfileStore (%APPDATA%\RoomEq),
│   │                       renommage intégré, DiagnosticsLog (journal par jour,
│   │                       logs/ à côté de l'exe, repli %LOCALAPPDATA%, jamais
│   │                       bloquant, interrupteur global de la version publique),
│   │                       AppFlavor (version test/public par nom d'exe),
│   │                       TelemetryConfig + DriveLogUploader (envoi Drive)
│   ├── RoomEq.Audio/       NAudio : moteur de lecture (WASAPI partagé / ASIO exclusif),
│   │   │                   rééchantillonnage WDL, adaptation de canaux, EQ inline, tap,
│   │   │                   capture micro float (format 48 kHz mono, repli adaptatif sur
│   │   │                   le format natif du périphérique), moteur de mesure pièce
│   │   │                   (async, multi-signaux segmentés, balayage + déconvolution,
│   │   │                   capture démarrée avant la lecture, détection de capture muette,
│   │   │                   un signal en échec ne bloque pas la mesure)
│   │   ├── Asio/           AsioManager : capacités des pilotes, panneau de contrôle
│   │   └── Routing/        Table de mixage : RingBuffer lock-free, ConsoleChannel
│   │                       (EQ de piste, fader, pan, mute/solo, crêtes), ConsoleMixer
│   │                       (bus master + EQ principal synchronisé, protections larsen),
│   │                       AudioDeviceCatalog (entrées/sorties, sonde par protocole),
│   │                       VirtualAudioProviders (carte RoomEq / loopback), DriverSetupManager
│   │                       découverte automatique de version sur la page officielle),
│   │                       VirtualAudioInstaller (téléchargement officiel multi-candidats,
│   │                       validation, élévation UAC fiabilisée, détection d'arrivée du pilote),
│   │                       DefaultDeviceSetter (API PolicyConfig, 3 IIDs en cascade,
│   │                       sortie ET entrée par défaut, confirmation par relecture), MixerStore
│   └── RoomEq.App/         UI Avalonia 11 (thème sombre studio) : PlotControl
│                           (courbes log-fréq/dB), MicCurveEditor (courbe de
│                           correction micro éditable à la souris), EqControl
│                           (poignées interactives), VuMeter (crêtes stéréo),
│                           6 onglets, MVVM (CommunityToolkit.Mvvm),
│                           HelpButton/HelpDialog (aide « ? »),
│                           FlavorDialog (bascule test → public au lancement),
│                           SignalCatalogDialog (signaux de test téléchargeables)
└── tests/RoomEq.Core.Tests/  150 tests xUnit : filtres (réponses exactes RBJ),
                               FFT (amplitude normalisée, inverse), plat du bruit blanc,
                               bandes d'octave, parseur .cal/CSV FR, designer de
                               correction (résonance +7 dB aplatie, bornes, préampli),
                               précision d'import (catalogue, montée monotone,
                               bandes/résidu douce vs très fine, résonance étroite),
                               signaux brun/violet (pentes spectrales), niveaux sinus,
                               balayage (générateur déterministe, déconvolution de
                               Farina sur pièce plate et pièce à 2 anomalies, rejet
                               capture muette, silences réels de pré/post-roulement,
                               diagnostics enrichis de l'exception), diagnostics
                               (niveaux crête/RMS de segments, journal de diagnostic
                               et son interrupteur global, détection de capture
                               muette), versions test/public (règle de nommage
                               des exécutables), croisement multi-signaux
                               (médiane robuste, pipeline balayage → correction),
                               planificateur automatique (anomalies retrouvées, bornes,
                               seuil, confiance, ANTI-DOUBLONS : boost borné réparti,
                               pics étroits proches, largeurs de bande), bibliothèque
                               micros (36 profils, technologies, recherche),
                               renommage presets/profils, signaux téléchargeables
                               (manifeste : JSON invalide, traversal, tailles,
                               SHA-256, doublons ; verrouillage d'URL ; bibliothèque
                               locale : SHA vérifié, noms refusés, index corrompu)
    tests/RoomEq.Audio.Tests/  78 tests xUnit : RingBuffer (continuité, saturation,
                               silence), loi de pan, fader, EQ de piste (passe-haut),
                               synchro EQ principal → bus master, persistance mixer.json,
                               protocoles virtuels (carte RoomEq + loopback, gestion
                               du paquet pilote local : pnputil install/désinstall)
                               officielles, ZipSlip, PE, recherche d'installateur,
                               découverte de version, seuils de taille, IIDs PolicyConfig),
                               guides, catalogue de signaux (unicités, amplitudes,
                               fréquences audibles, balayages, item par défaut,
                               conversion curseur → volume Windows)
```

Chaîne de traitement : `source → resample → canaux → boucle → EQ → (volume × préampli) → tap RTA → sortie`.

Chaîne de routage (onglet Mixage & routage) : `capture/loopback → RingBuffer → resample → stéréo → EQ de piste → fader × pan × mute/solo → Σ bus → EQ principal (synchro temps réel) → fader master → sortie`.

**Mesure multi-signaux** : chaque signal est joué et capturé tour à tour (0,5 s d'attaque et 0,05–0,25 s de fin écartés par segment) → Welch (Hann, 16 384 points, recouvrement 50 %) par segment de bruit ; pour le **balayage** : régénération exacte de la référence (LogSweepGenerator, phases en forme fermée) → **déconvolution de Farina** (H = Y·conj(X)/|X|², inverse régularisé) → réponse impulsionnelle → recherche du pic (retard positif ou replié), fenêtrage son direct + 0,4 s de réverbération (harmoniques ESS écartées) → FFT → niveaux par bandes 1/6 d'octave → correction micro → retrait de la pente spectrale théorique du signal → alignement (moyenne 200 Hz – 8 kHz) → **médiane inter-signaux par bande** + dispersion → **AutoBandPlanner** : placement glouton de peaking (fréquence = plus grand résidu hors des zones d'exclusion, Q = largeur à mi-hauteur, gain = résidu borné et dosé par la confiance inter-signaux), **zones d'exclusion anti-doublon** (demi-largeurs de bande, plancher 1/12 oct — un pic qui violerait l'espacement est masqué et la recherche reprend ailleurs) + passe finale de déduplication, simulation avec la vraie réponse des biquads, arrêt quand la prédiction touche la cible → préampli de sécurité.

Mode « bruit blanc seul » : Welch → lissage 1/6 oct → correction micro → alignement → niveaux 1/3 d'octave → correction bornée par bande (Q ≈ 4,32) → préampli → prédiction (chemin historique conservé).

## Dépannage et questions fréquentes

**La courbe de calibration ne s'affiche pas.**
**Des contrôles sont coupés à droite (menu des signaux…).** Quand la fenêtre est étroite, la barre supérieure, la barre de contrôle de l'Analyseur et la barre de routage du Mixage **défilent horizontalement** : molette + Maj, geste deux doigts du pavé tactile, ou la barre de défilement sous la barre concernée. Sur un écran 1920 px tout tient sans défilement.
**Quelle différence entre les versions test et public ?** Le même binaire, seul le nom de l'exécutable change le comportement : `RoomEq-test.exe` journalise tout et envoie vers le Drive (pour le diagnostic à distance) ; `RoomEq-public.exe` ne journalise rien et n'envoie rien. Au lancement de la version test, une boîte de dialogue propose de basculer vers la version publique si elle est à côté. La version en cours est affichée dans la barre de titre.

**Le volume Windows de la carte virtuelle ne change rien — normal ?** Oui : « RoomEq Input » est un tuyau logiciel, son fader est purement décoratif. Réglez le volume de la carte physique qui rend le son : le **curseur Volume de la barre du haut** (de 0 à 100 % il règle directement la carte physique routée), **Ctrl+Alt+↑ / ↓** (±5 %), **Ctrl+Alt+M** (sourdine), ou le curseur « Vol. carte physique » de la tranche MASTER. Les deux curseurs sont **toujours synchronisés** : déplacer l'un met l'autre à la même position, quel que soit l'ordre — et un changement fait hors de l'application (mixeur Windows, touches du casque) est suivi à l'écran.

**Les deux curseurs de volume n'affichaient pas la même chose — corrigé ?** Oui (1.9.1) : une seule source de vérité (le volume Windows de la carte physique) — chaque commande (barre du haut, tranche MASTER, raccourcis clavier) y écrit, puis tous les affichages relisent la carte : la synchro ne dépend plus de l'ordre de manipulation.

**D'où viennent les signaux du menu « Téléchargés » ?** Du dépôt GitHub officiel du projet (dossier `signaux/`, fichiers WAV publiés avec les sources). Le catalogue est téléchargé à l'ouverture de la fenêtre « Télécharger d'autres… », chaque fichier est vérifié (taille + SHA-256 du manifeste) puis enregistré dans `%APPDATA%\RoomEq\signaux` ; les fichiers apparaissent ensuite dans le menu déroulant, section « Téléchargés ». Sans connexion Internet, seuls les signaux générés par l'application restent disponibles.

**Mon micro est stéréo mais une voie est muette/parasite ?** Dans l'onglet Calibration, un sélecteur « Canal du micro » apparaît pour les micros multi-canaux : choisissez « Canal 1 seul (mono) » (ou 2) — le mixage stéréo diluerait le signal utile.

**Que contient la télémétrie envoyée ?** Uniquement le texte du journal de diagnostic (phases de calibration, formats, niveaux, erreurs avec pile) — jamais de contenu audio. Les fichiers sont nominatifs par machine (`<machine>-roomeq-AAAA-MM-JJ.log`) et consultables dans le dossier Drive « journaux-app ».

**Où est passée l'aide ?** Chaque section affiche un petit « ? » : survol = infobulle, clic = boîte de dialogue complète.

Depuis la 1.6.1 (et encore plus en 1.7.0), toute erreur s'affiche en rouge dans l'onglet Calibration et est tracée dans le journal (`logs\roomeq-AAAA-MM-JJ.log`, chemin affiché en bas du panneau). Causes classiques : micro virtuel choisi dans la liste (« RoomEq Output », signalé — choisissez votre micro physique) ; micro muet ou niveau d'entrée à zéro dans Windows ; format du micro refusé (RoomEq retente automatiquement avec le format natif) ; signal ignoré en échec — la mesure continue avec les autres et la raison est listée dans le résumé.

**La calibration dit « micro silencieux ».**
Vérifiez dans les réglages son Windows que le micro sélectionné est le périphérique d'entrée actif, non muet, avec un niveau d'entrée correct (le VU doit bouger pendant le bruit). Dans RoomEq, utilisez l'onglet Analyseur en source « Micro » pour valider que le signal arrive.

**Aucun pilote ASIO n'apparaît.**
Installez le pilote du fabricant ou ASIO4ALL, puis « Rafraîchir » dans l'onglet ASIO. Un pilote 32 bits ne peut pas être chargé par une application 64 bits : utilisez la version x64 du pilote.

**La lecture ASIO échoue alors que le pilote est listé.**
Le pilote est peut-être occupé (autre application en exclusif) : fermez-la. Essayez aussi de changer la taille de buffer dans le panneau de contrôle du pilote (onglet ASIO).

**Le son écrête après calibration.**
La correction comporte des boosts ; le préampli calculé (-boost max) devrait l'éviter. Si le fichier source est déjà fort, baissez le volume maître ou relancez une calibration avec un boost max plus faible.

**La correction semble trop agressive dans les aigus.**
Utilisez un micro de mesure avec son fichier de calibration, augmentez la durée de mesure (25–30 s), et gardez la coupe max à 12 dB plutôt que d'augmenter le boost.

**EQ de tout le système ?**
Oui — c'est le rôle de l'onglet **Mixage & routage** : cliquez « Installer le pilote RoomEq… » si la carte virtuelle manque (paquet intégré, aucun téléchargement) : Windows bascule tout seul vers « RoomEq Input » et le routage démarre. Sinon, cliquez « Router Windows vers l'EQ ». Voir la section dédiée plus haut.

**L'installation automatique échoue ou l'invite UAC n'apparaît pas.**
Le pilote est livré **avec** l'application (dossier `driver\` du zip, `Program Files\RoomEq\driver\` en installation) : aucun téléchargement n'a lieu. Concernant l'invite administrateur, trois cas sont normaux : RoomEq lancé **en administrateur** → l'installation hérite des droits sans invite ; UAC réglé sur « **ne jamais notifier** » → élévation silencieuse ; UAC refusé → un message explicite s'affiche. Après installation, si « RoomEq Input / RoomEq Output » n'apparaissent pas, redémarrez Windows puis cliquez « ⟳ ».

> Pilote **signé-test** : si l'installation aboutit mais que les périphériques n'apparaissent pas, activez le mode test de Windows (`INSTALLATION-PILOTE.md`) — c'est la signature, pas l'installation, qui bloque alors le démarrage du pilote.

**Je veux retrouver mes périphériques d'origine après un routage.**
C'est automatique : à l'arrêt du routage (« ■ Arrêter le routage »), la sortie et l'entrée par défaut mémorisées avant la bascule sont restaurées. Si l'application a été fermée pendant le routage, démarrez puis arrêtez le routage une fois (ou utilisez le panneau son).

---

## Licence et distribution

RoomEq est distribué sous **licence MIT** : vous pouvez librement utiliser, étudier, modifier et redistribuer le logiciel — y compris à des fins commerciales — à la seule condition de conserver la mention de copyright et le texte de licence dans les copies ([LICENSE](LICENSE)).

- **Utilisateur final** : installez, utilisez, partagez sans restriction.
- **Développeur** : le code (DSP, calibration, audio, Avalonia) peut être réutilisé dans vos propres projets, y compris commerciaux, sous réserve d'attribution.
- **Composants tiers** (Avalonia, NAudio, CommunityToolkit.Mvvm…) : ils restent sous leurs licences permissives respectives, détaillées dans [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

Sous licence MIT, la valeur ajoutée d'une distribution officielle repose sur la **commodité** : version signée du Microsoft Store avec mises à jour automatiques, packaging prêt à l'emploi, support, documentation et suivi du projet. Les pistes de distribution et de monétisation (Windows Store, vente directe, winget, portages) sont décrites dans [docs/DISTRIBUTION.md](docs/DISTRIBUTION.md).
