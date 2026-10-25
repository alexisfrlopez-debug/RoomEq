# Dépannage — « Windows empêche l'installation » du MSI RoomEq

Ce guide couvre tous les cas où Windows bloque l'installation de
`RoomEq-2.0.0-win-x64.msi`. **Identifiez le message exact affiché à l'écran**
dans la table ci-dessous, puis suivez la section correspondante.

| Ce que vous voyez | Cause | Section |
|---|---|---|
| « **Windows a protégé votre ordinateur** » (fond bleu, SmartScreen) | MSI téléchargé d'Internet + non signé | [Cas 1](#cas-1--windows-a-protégé-votre-ordinateur-smartscreen) |
| « **Windows ne peut pas vérifier l'éditeur** de ce logiciel » | Marque « du web » sur le fichier | [Cas 2](#cas-2--windows-ne-peut-pas-vérifier-léditeur) |
| « **Opération annulée** », le fichier **disparaît** ou « Menace détectée » | Antivirus / Defender (quarantaine) | [Cas 3](#cas-3--antivirus--defender-bloque-ou-supprime-le-msi) |
| « **Modifications non autorisées bloquées** » (msiexec.exe / SrTasks.exe) | Accès contrôlé aux dossiers (anti-ransomware) | [Cas 3bis](#cas-3bis--modifications-non-autorisées-bloquées-accès-contrôlé-aux-dossiers) |
| « **Vous devez disposer de droits d'administrateur** » ou invite UAC sans mot de passe valide | Session non administrateur | [Cas 4](#cas-4--droits-administrateur-manquants) |
| Message RoomEq : « **Impossible d'activer le mode de signature de test** de Windows (bcdedit…) » | Secure Boot actif dans le BIOS/UEFI | [Cas 5](#cas-5--secure-boot-actif-mode-test-impossible) |
| « Cette application est bloquée par la **stratégie de votre organisation** » | PC géré (entreprise/école) ou mode S | [Cas 6](#cas-6--pc-géré-par-une-organisation) |
| « **L'installation a été interrompue prématurément** » ou code **1603 / 1625 / 1730** | Échec en cours d'installation | [Cas 7](#cas-7--échec-en-cours-dinstallation--journal-détaillé) |
| **La carte son virtuelle n'apparaît pas** après installation du pilote (ou bouton « Installer le pilote » de l'application) | Périphérique non créé / mode test absent ou refusé / Secure Boot | [Cas 8](#cas-8--la-carte-son-virtuelle-napparaît-pas) |
| **Revenir au mode normal** de Windows (supprimer le filigrane « Mode de test ») | Mode test actif, à couper | [Revenir au mode normal](#revenir-au-mode-normal-de-windows) |

> **Rappel** : le MSI RoomEq actuel est un build de **test** : il embarque un
> pilote signé-test (pas encore signé Microsoft) et l'installateur lui-même
> n'a pas de signature Authenticode. Windows réagit donc comme pour tout
> logiciel inconnu téléchargé d'Internet : il met des garde-fous, mais
> **tous se contournent en quelques clics** quand on sait où cliquer.

> **Alternative sans MSI (oct. 2026)** : une distribution « exécutables »
> existe désormais — `RoomEq-…-win-x64.exe` (application portable, un seul
> fichier), `RoomEqAppSetup-….exe` (même application en version installable :
> raccourcis + lancement automatique au démarrage de Windows, sans UAC) et
> `RoomEqDriverSetup-….exe` (pilote + ASIO embarqués, une invite UAC, aucune
> chaîne msiexec donc plus rien à débloquer de ce qui précède).
> Voir `INSTALLATION-PILOTE.md`, section « exécutables ». Seuls restent le
> « Débloquer » du premier lancement (cas 1) et la contrainte Secure Boot
> (cas 5), qui disparaîtront avec la signature Microsoft.

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

## Cas 3bis — « Modifications non autorisées bloquées » (accès contrôlé aux dossiers)

**Cas confirmé par capture d'écran (17:09/17:11).** Notification exacte
de Sécurité Windows :

> **« Modifications non autorisées bloquées »**
> L'accès contrôlé aux dossiers a bloqué `C:\Windows\System32\msiexec.exe`
> (puis `SrTasks.exe`) d'apporter des modifications.

**Cause** : la protection **anti-ransomware** de Defender (« Accès
contrôlé aux dossiers », CFA) bloque tout processus non expressément
approuvé qui touche aux zones qu'elle protège — y compris des binaires
Windows légitimes : ici l'installateur lui-même (`msiexec.exe`) et la
création du **point de restauration système** (`SrTasks.exe`). C'est une
classe de faux positifs connue de cette fonctionnalité (très agressive
par conception) ; cela n'indique pas que le MSI est malveillant. La
chaîne d'installation d'un paquet non signé (point de restauration →
actions différées dans un processus msiexec enfant) la déclenche
facilement.

**Désactivation temporaire (recommandé, ~2 minutes) :**

1. **Paramètres → Confidentialité et sécurité → Sécurité Windows →
   Ouvrir Sécurité Windows** (ou zone de notifications → icône bouclier).
2. **Protection contre les virus et menaces** → descendre jusqu'à
   **« Protection contre les ransomware »** → **Gérer la protection
   contre les ransomware**.
3. Basculez **« Accès contrôlé aux dossiers » sur Désactivé**.
4. Relancez le MSI (clic droit → **Exécuter en tant qu'administrateur**).
5. Une fois l'installation terminée et la machine redémarrée :
   **réactivez** « Accès contrôlé aux dossiers » (la protection est
   désactivée uniquement pendant ces quelques minutes).

**Variante chirurgicale (garder la protection active)** : même écran →
« **Autoriser une application via l'accès contrôlé aux dossiers** » →
**Ajouter une application autorisée** → sélectionnez
`C:\Windows\System32\msiexec.exe`. Si une nouvelle notification cite
`SrTasks.exe` (point de restauration), ajoutez-le de la même façon
(`C:\Windows\System32\SrTasks.exe`). Les applications récemment
bloquées figurent aussi dans la liste du même écran avec un bouton
« + Ajouter ».

> Astuce PowerShell (administrateur) pour la variante :
> `Add-MpPreference -ControlledFolderAccessAllowedApplications
> "C:\Windows\System32\msiexec.exe"`

### Si la commande PowerShell ne suffit pas (cas confirmé en pratique)

Autoriser `msiexec.exe` seul ne suffit **pas** pour ce MSI, pour deux raisons :

1. **La chaîne d'installation fait intervenir d'autres processus** que
   Windows bloque un par un : `SrTasks.exe` (point de restauration, déjà
   constaté sur la capture), `bcdedit.exe` (mode test), `pnputil.exe`
   (pilote). Chacun déclenche sa propre notification « Modifications non
   autorisées bloquées » et fait échouer son étape.
2. **La commande ne produit aucun effet si PowerShell n'était pas ouvert
   en administrateur** : elle est alors refusée (« accès refusé ») et rien
   n'est ajouté à la liste blanche.

Deux issues, de la plus simple à la plus complète :

**Voie 1 — couper la protection pendant l'installation (4 clics, fiable)** :
suivez le chemin **A** ci-dessus (Accès contrôlé aux dossiers →
**Désactivé**), installez, redémarrez si proposé, puis **réactivez** la
protection. C'est la méthode recommandée : elle neutralise d'un coup tous
 les processus de la chaîne, y compris ceux qu'on ne voit pas.

**Voie 2 — le script `Installer-RoomEq.ps1` v2 (tout-en-un)** : clic droit
→ *Exécuter avec PowerShell*. Il diagnostique la machine **avant**
d'installer (état de l'anti-ransomware, **Secure Boot**, mode test,
pilote, périphérique, application), coupe l'accès contrôlé aux dossiers
le temps de l'installation, la **réactive automatiquement à la fin**,
installe avec journal détaillé, et affiche un **rapport à copier-coller**
qui contient le verdict exact de l'échec le cas échéant.

> ⚠️ **Après la levée du blocage anti-ransomware, l'échec peut changer de
> visage** : si l'installateur affiche « *Impossible d'activer le mode de
> signature de test de Windows (bcdedit…)* », ce n'est plus l'antivirus —
> c'est le **Secure Boot** (cas 5) : le mode test est cryptographiquement
> impossible tant qu'il est actif. Le script v2 détecte ce cas dès son
> diagnostic (ligne `Secure Boot : ACTIF` du rapport).

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

Le script **`Installer-RoomEq.ps1` v2** (à côté de ce guide) automatise tout :
diagnostic préalable (anti-ransomware, Secure Boot, mode test, pilote,
périphérique), gestion de l'accès contrôlé aux dossiers le temps de
l'installation, déblocage, installation avec journal, et rapport final à
copier-coller en cas d'échec.

---

## Récapitulatif express (procédure recommandée)

```bat
1. Clic droit sur le MSI → Propriétés → cocher « Débloquer » → OK
2. Double-clic → UAC → Oui
3. (écran bleu SmartScreen ?) « Informations complémentaires » → « Exécuter quand même »
4. Notification « Modifications non autorisées bloquées » ? → couper l'accès
   contrôlé aux dossiers pendant l'installation (cas 3bis, voie 1) → réactiver après
5. Message « mode de signature de test impossible » ? → cas 5 (Secure Boot, BIOS)
6. Redémarrer quand proposé → cartes « RoomEq Input/Output » dans le panneau son
```

**Ou en une seule manipulation** : clic droit sur `Installer-RoomEq.ps1` →
*Exécuter avec PowerShell* — le script enchaîne déblocage, gestion de
l'anti-ransomware, installation et rapport final tout seul.

## La solution durable (pour la distribution publique)

Tous ces garde-fous disparaissent quand :

1. le **pilote** est signé Microsoft (attestation, `INSTALLATION-PILOTE.md`
   voie B) → plus de mode test, plus de certificat à importer, plus de
   redémarrage ;
2. le **MSI** est signé Authenticode (le même certificat EV sert aux deux)
   → plus de SmartScreen « éditeur inconnu ».

D'ici là, ce guide est la référence : un utilisateur qui lit la table en
haut s'en sort en moins d'une minute.

---

## Cas 8 — La carte son virtuelle n'apparaît pas

Après `RoomEqDriverSetup.exe` (ou le bouton « Installer le pilote RoomEq » de
l'onglet Mixage), **« RoomEq Input » / « RoomEq Output »** ne figurent pas
dans les périphériques audio. Dans l'ordre des fréquences :

1. **Lancez le diagnostic automatique** (invite administrateur, ne modifie
   rien) :

   ```
   RoomEqDriverSetup.exe --status
   ```

   Depuis la v2.1.0, il affiche l'état complet (Secure Boot, mode test,
   magasin de pilotes, périphérique avec son **code de problème PnP**,
   service, ASIO) puis un **VERDICT** qui désigne la cause et l'action.

2. **Causes typiques** (le verdict les distingue pour vous) :

   | Verdict | Cause | Action |
   |---|---|---|
   | « Windows n'est PAS en mode test… refuse de charger ce pilote signé-test (code 52) » | Mode test absent ou refusé à l'installation — c'est le comportement **voulu** si vous avez répondu non | Trois voies : relancer `--install` et accepter le mode test ; rester sans carte virtuelle (lecteur, calibration et boucle WASAPI fonctionnent) ; attendre le pilote signé Microsoft (`docs/SIGNATURE-ET-STORE.md`) |
   | « le paquet est dans le magasin mais le PÉRIPHÉRIQUE n'existe pas » | Installation partielle : `pnputil` seul ne crée jamais le périphérique d'un pilote `ROOT\…` (ancien bouton de l'application avant v2.1.0) | Relancer `RoomEqDriverSetup.exe --install` (étape 5 = création du périphérique) |
   | « le mode test est actif mais le Secure Boot l'ignore » | Secure Boot actif : Windows refuse le mode test **par conception** | Désactiver Secure Boot dans le BIOS/UEFI puis redémarrer ; ou `--mode-normal` pour faire marche arrière |
   | « tout semble correct » | La carte existe mais est masquée ou désactivée | `mmsys.cpl` → clic droit dans la liste → **« Afficher les périphériques désactivés »** → clic droit sur RoomEq → **Activer** |

3. **Le bouton de l'application** : depuis la v2.1.0, il lance
   `RoomEqDriverSetup.exe` quand celui-ci est présent à côté de l'application
   (cas de la version installable, qui le dépose automatiquement, ou des deux
   exécutables dans le même dossier). Avant cette version, il ne faisait
   qu'un `pnputil /add-driver` — d'où des installations « réussies » sans
   carte.

## Revenir au mode normal de Windows

Vous avez activé le mode test (directement ou via une installation
antérieure) et voulez le couper — y compris le filigrane « Mode de test » en
bas à droite du bureau :

1. **Le plus simple** : `RoomEqDriverSetup.exe --mode-normal`
   - coupe le mode test (`bcdedit /set testsigning off`) ;
   - propose le redémarrage (le filigrane disparaît ensuite définitivement) ;
   - propose le retrait complet du pilote signe-test, devenu inutilisable en
     mode normal.

2. **À la main** (invite de commandes administrateur) :

   ```
   bcdedit /set testsigning off
   ```

   puis redémarrez. Sans le mode test, la carte virtuelle RoomEq ne peut pas
   charger (pilote signé-test) : c'est normal, voir le Cas 8 pour les trois
   voies possibles.

3. La désinstallation du pilote (`--uninstall`) **propose** désormais de couper
   le mode test à la fin.
