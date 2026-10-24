# Dépannage — « Windows empêche l'installation » du MSI RoomEq

Ce guide couvre tous les cas où Windows bloque l'installation de
`RoomEq-2.0.0-win-x64.msi`. **Identifiez le message exact affiché à l'écran**
dans la table ci-dessous, puis suivez la section correspondante.

| Ce que vous voyez | Cause | Section |
|---|---|---|
| « **Windows a protégé votre ordinateur** » (fond bleu, SmartScreen) | MSI téléchargé d'Internet + non signé | [Cas 1](#cas-1--windows-a-protégé-votre-ordinateur-smartscreen) |
| « **Windows ne peut pas vérifier l'éditeur** de ce logiciel » | Marque « du web » sur le fichier | [Cas 2](#cas-2--windows-ne-peut-pas-vérifier-léditeur) |
| « **Opération annulée** », le fichier **disparaît** ou « Menace détectée » | Antivirus / Defender | [Cas 3](#cas-3--antivirus--defender-bloque-ou-supprime-le-msi) |
| « **Vous devez disposer de droits d'administrateur** » ou invite UAC sans mot de passe valide | Session non administrateur | [Cas 4](#cas-4--droits-administrateur-manquants) |
| Message RoomEq : « **Impossible d'activer le mode de signature de test** de Windows (bcdedit…) » | Secure Boot actif dans le BIOS/UEFI | [Cas 5](#cas-5--secure-boot-actif-mode-test-impossible) |
| « Cette application est bloquée par la **stratégie de votre organisation** » | PC géré (entreprise/école) ou mode S | [Cas 6](#cas-6--pc-géré-par-une-organisation) |
| « **L'installation a été interrompue prématurément** » ou code **1603 / 1625 / 1730** | Échec en cours d'installation | [Cas 7](#cas-7--échec-en-cours-dinstallation--journal-détaillé) |

> **Rappel** : le MSI RoomEq actuel est un build de **test** : il embarque un
> pilote signé-test (pas encore signé Microsoft) et l'installateur lui-même
> n'a pas de signature Authenticode. Windows réagit donc comme pour tout
> logiciel inconnu téléchargé d'Internet : il met des garde-fous, mais
> **tous se contournent en quelques clics** quand on sait où cliquer.

---

## Cas 1 — « Windows a protégé votre ordinateur » (SmartScreen)

C'est le cas **le plus fréquent**. Le MSI a été téléchargé depuis Google
Drive ou GitHub : Windows lui colle une « marque du web » (Mark of the Web)
et, comme il n'est pas signé par un éditeur connu, Microsoft Defender
SmartScreen l'arrête avant même le lancement.

**Contournement immédiat (30 secondes) :**

1. Dans la fenêtre bleue « Windows a protégé votre ordinateur », cliquez sur
   le lien **« Informations complémentaires »** (petit texte bleu).
2. Un second écran décrit « RoomEq-2.0.0-win-x64.msi » : cliquez sur
   **« Exécuter quand même »**.
3. L'installation démarre : acceptez l'invite **Contrôle de compte
   d'utilisateur** (mot de passe administrateur), puis suivez l'assistant.

**Ou mieux — supprimer la marque du web avant de lancer (recommandé) :**

1. Clic droit sur `RoomEq-2.0.0-win-x64.msi` → **Propriétés**.
2. En bas de l'onglet Général, cochez **« Débloquer »**.
3. **OK**, puis double-cliquez normalement : plus aucun avertissement.

En PowerShell (session administrateur) :

```powershell
Unblock-File "$env:USERPROFILE\Downloads\RoomEq-2.0.0-win-x64.msi"
```

> Le fichier téléversé est identique ; « débloquer » ne modifie que la
> marque de sécurité, pas le contenu. La somme de contrôle SHA-256 du MSI
> v2.0.0 est fournie dans le dossier de sauvegarde Drive.

---

## Cas 2 — « Windows ne peut pas vérifier l'éditeur »

Variante du cas 1 (attachement téléchargé) : la boîte « Avertissement de
sécurité — Ouverture de fichier » propose déjà un bouton **« Exécuter »**.
Cliquez simplement dessus, ou appliquez d'abord la procédure **Débloquer**
du cas 1 pour ne plus voir cet avertissement.

---

## Cas 3 — Antivirus / Defender bloque ou supprime le MSI

Symptômes : le téléchargement est « Opération annulée » dans le navigateur,
le fichier disparaît du dossier Téléchargements, ou une notification
« Menace détectée / PUA:Win32… » s'affiche.

Explication : le MSI contient un pilote noyau de test + une DLL d'action
personnalisée non signée — un profil courant de faux positif pour les
heuristiques antivirus, car certains logiciels malveillants font pareil
(nos actions : certificat, bcdedit, pnputil, création de périphérique).

**Procédure :**

1. Sécurité Windows → **Historique de protection** → repérez la détection
   sur le MSI → **Restaurer** (la quarantaine rend le fichier).
2. Vérifiez l'intégrité du fichier restauré (taille = **37 408 768 octets**
   pour la v2.0.0, ou comparez le SHA-256 avec celui du dossier Drive).
3. **Sécurité Windows → Protection contre les ransomware et les virus →
   Gérer les paramètres → Exclusions → Ajouter une exclusion** :
   - soit le **dossier** contenant le MSI le temps de l'installation,
   - soit le fichier `RoomEq-2.0.0-win-x64.msi` lui-même.
4. Relancez l'installation, puis **retirez l'exclusion** ensuite.
5. Un antivirus tiers (Avast, Kaspersky…) : même logique — restaurez le
   fichier depuis sa quarantaine et ajoutez une exclusion temporaire.

> Pour information : la chaîne de build est publique (GitHub Actions) et
> le paquet driver CI (`roomeq-driver-2.0.0-ci.zip`, Drive) contient le
> `.cer` de test si vous voulez auditer la signature avant d'exécuter.

---

## Cas 4 — Droits administrateur manquants

Le MSI installe pour toute la machine (`perMachine` : Program Files,
pilote, magasins de certificats) : l'invite UAC est **normale et
obligatoire**.

- Compte administrateur : acceptez l'invite UAC (ou clic droit sur le MSI →
  **Exécuter en tant qu'administrateur**).
- Compte standard : Windows demande le mot de passe d'un administrateur ;
  sans cela, demandez à l'administrateur du PC de lancer l'installation.
- Installation silencieuse en invite admin :

```bat
msiexec /i "RoomEq-2.0.0-win-x64.msi" /l*v "%TEMP%\RoomEq-install.log"
```

---

## Cas 5 — Secure Boot actif (mode test impossible)

Message exact de l'installateur RoomEq :

> « Impossible d'activer le mode de signature de test de Windows (bcdedit…).
> Cause la plus fréquente : le Secure Boot est actif — désactivez-le dans
> le BIOS/UEFI puis relancez l'installation. »

Windows refuse le mode « testsigning » quand le **Secure Boot** est actif
(vérification cryptographique du boot). L'installation est **annulée
proprement** (rollback : rien n'est cassé). Deux options :

**Option A — désactiver le Secure Boot (machine de test, 5 min) :**

1. Redémarrez et entrez dans le BIOS/UEFI (touche **Suppr**, **F2**, **F10**
   ou **Échap** selon la carte mère — souvent indiqué au démarrage).
2. Cherchez **Secure Boot** (onglet Security / Boot / Authentication).
3. Passez-le à **Disabled** (sur certains PC : d'abord « OS Type → Other
   OS », ou décocher « Attempt Secure Boot »). BitLocker éventuellement
   actif : Windows demandera la clé de récupération au redémarrage —
   **récupérez-la d'abord** (Gérer mon compte Microsoft → Appareils →
   Clé de récupération BitLocker) ou suspendez BitLocker
   (`manage-bde -protectors -disable C:`) avant de modifier.
4. Sauvegardez (F10), redémarrez, relancez le MSI.

**Option B — garder le Secure Boot** : il faut un pilote signé Microsoft
(attestation signing, voir `INSTALLATION-PILOTE.md` voie B — certificat EV
~300 €/an). Tant que ce n'est pas fait, un pilote signé-test **ne peut
pas** charger sur une machine Secure Boot actif : c'est une limite de
sécurité de Windows, pas un bug de l'installateur.

> Après réussite : si la carte son n'apparaît toujours pas après le
> redémarrage, vérifiez l'**intégrité de la mémoire (HVCI)** : Paramètres →
> Confidentialité et sécurité → Sécurité de l'appareil → Isolation du
> noyau → désactivez « Intégrité de la mémoire », redémarrez, réinstallez.

---

## Cas 6 — PC géré par une organisation

« Cette application est bloquée par la stratégie de votre organisation »
(AppLocker / WDAC / mode S / stratégie DisableMSI) : le PC est verrouillé
par votre entreprise ou votre école — c'est précisément le but de ces
politiques, et aucun contournement local n'est légitime.

- **PC personnel en mode S** : Paramètres → Système → Activation → sortir
  du mode S (gratuit, définitif) puis réessayez.
- **PC professionnel/scolaire** : demandez à votre service informatique,
  ou testez sur un PC personnel.

---

## Cas 7 — Échec en cours d'installation (journal détaillé)

« L'installation s'est terminée prématurément à cause d'une erreur » /
code 1603, 1625, 1730, 1618 (« autre installation en cours »)…

Relancez avec **journal complet** :

```bat
msiexec /i "RoomEq-2.0.0-win-x64.msi" /l*v "%TEMP%\RoomEq-install.log"
```

Puis ouvrez `%TEMP%\RoomEq-install.log` et cherchez les lignes
**`RoomEq CA`** : elles tracent chaque étape (certificat, mode test,
pnputil, périphérique) en français, avec le point d'échec exact :

- `RoomEq CA : certificat de test importé (Root + TrustedPublisher)` ✓
- `RoomEq CA : mode testsigning déjà actif` ou `activé — redémarrage requis` ✓
- `RoomEq CA : pilote ajouté au magasin (oemNN.inf)` ✓
- `RoomEq CA : périphérique ROOT\ROOMEQVAD créé` ✓
- `RoomEq CA ERREUR : …` ← la ligne qui explique l'échec

Codes courants :

| Code | Sens | Piste |
|---|---|---|
| 1603 | Erreur fatale générique | Lire la ligne `RoomEq CA ERREUR` (souvent cas 3 ou 5) |
| 1618 | Autre installation en cours | Attendez/fermez l'autre installateur, réessayez |
| 1625 | Installation interdite par le système | Cas 6 (politique) |
| 1730 | Paquet non approuvé | Cas 6 ou antivirus |
| 3010 | **Succès** + redémarrage requis | Normal avec le mode test : redémarrez |

Le script **`Installer-RoomEq.ps1`** (à côté de ce guide) automatise :
détection du MSI, déblocage, installation avec journal, et ouverture du
journal en cas d'échec.

---

## Récapitulatif express (procédure recommandée)

```bat
1. Clic droit sur le MSI → Propriétés → cocher « Débloquer » → OK
2. Double-clic → UAC → Oui
3. (écran bleu SmartScreen ?) « Informations complémentaires » → « Exécuter quand même »
4. Si Secure Boot le bloque : BIOS/UEFI → Secure Boot → Disabled → relancer le MSI
5. Redémarrer quand proposé → cartes « RoomEq Input/Output » dans le panneau son
```

## La solution durable (pour la distribution publique)

Tous ces garde-fous disparaissent quand :

1. le **pilote** est signé Microsoft (attestation, `INSTALLATION-PILOTE.md`
   voie B) → plus de mode test, plus de certificat à importer, plus de
   redémarrage ;
2. le **MSI** est signé Authenticode (le même certificat EV sert aux deux)
   → plus de SmartScreen « éditeur inconnu ».

D'ici là, ce guide est la référence : un utilisateur qui lit la table en
haut s'en sort en moins d'une minute.
