# Installation et signature du pilote « RoomEq Virtual Audio »

Ce document couvre l'installation du pilote de la carte son virtuelle RoomEq
(`roomeqvad`) et la **signature**, seule condition pour qu'un pilote Windows
10/11 64 bits soit accepté par le système. Deux voies sont prévues, au choix :

| Voie | Coût | Usage | Mise en place |
|---|---|---|---|
| **A. Signature de test** (intégrée aux scripts et à la CI) | Gratuit | Machines de test / développement | 5 minutes, ce document |
| **B. Attestation Microsoft** (officielle) | Certificat à signature EV (~300 €/an) + compte | Distribution au grand public | Ce document + Hardware Dev Center |

> **Pourquoi ?** Windows refuse par défaut tout pilote noyau non signé par
> Microsoft (politique de sécurité). Les produits équivalents du marché
> (câbles audio virtuels) sont signés pour cette même raison. Tant que le
> projet n'a pas de certificat EV, les builds officiels de la CI sont
> **signés-test**.

---

## 1. Installer le pilote sur une machine (application déjà installée)

### Nouvelle voie (oct. 2026) : la distribution « exécutables », sans MSI

Face aux blocages de la chaîne Windows Installer chez certains utilisateurs
(SmartScreen, puis anti-ransomware « accès contrôlé aux dossiers » qui
interceptait `msiexec.exe` et `SrTasks.exe`), une distribution alternative
**sans MSI** est produite par la CI :

| Fichier | Rôle | Privilèges |
|---|---|---|
| `RoomEq-<version>-win-x64.exe` | **L'application**, portable, auto-contenue en **un seul fichier** (~45 Mo) : aucun installateur, aucun UAC, lancez-la depuis n'importe où (Bureau, dossier, clé USB). | Aucun |
| `RoomEqAppSetup-<version>.exe` | **La même application, en version installable** (l'exe portable est embarqué à l'intérieur, ~45 Mo) : installe dans `%LOCALAPPDATA%\Programs\RoomEq` **sans UAC**, crée les raccourcis Menu Démarrer/Bureau, propose le **lancement automatique au démarrage de Windows** et s'ajoute à « Applications & fonctionnalités » avec désinstalleur intégré. | Aucun |
| `RoomEqDriverSetup-<version>.exe` | **La carte son virtuelle + le pilote ASIO** en un seul fichier : les fichiers pilote sont embarqués à l'intérieur. Une invite UAC (unique), une console qui affiche chaque étape, puis propose le redémarrage. | Administrateur |

**Installation** : exécutez `RoomEqDriverSetup` (acceptez l'UAC), redémarrez
quand il le propose, puis lancez `RoomEq-…-win-x64.exe` (portable) **ou**
`RoomEqAppSetup` (installée). Aucune manipulation PowerShell, aucun
`msiexec` — les étapes (certificat de test, mode test, magasin de pilotes,
périphérique `ROOT\ROOMEQVAD`, ASIO dans `C:\Program Files\RoomEq` + clés
de registre) s'enchaînent dans la console avec un message par étape.

Options en ligne de commande de `RoomEqDriverSetup` :
`--status` (état de la machine : Secure Boot, mode test, pilote, périphérique,
ASIO — sans rien modifier) et `--uninstall` (retrait complet : périphérique,
paquet, certificat, ASIO — non bloquant).

**Version installable de l'application (`RoomEqAppSetup`)** : double-cliquez,
répondez aux deux questions (raccourci Bureau, démarrage automatique — Oui
par défaut) et c'est fini — l'application se retrouve dans le Menu Démarrer,
« Applications & fonctionnalités » et, si accepté, se lance à chaque ouverture
de session (visible et désactivable dans le Gestionnaire des tâches →
« Applications au démarrage », ou via `RoomEqAppSetup --uninstall` qui retire
raccourcis, démarrage auto et fichiers — les réglages et mesures dans
`%APPDATA%\RoomEq` sont conservés). Options : `--status`, `--uninstall`,
`--no-autostart`, `--no-desktop`, `--help`. Réinstallation sans risque : tout
est écrasé proprement (mise à jour).

**Limites inchangées** : le pilote reste signé-test → le **Secure Boot**
doit être désactivé pour que le mode test puisse s'activer (le programme le
détecte et l'explique avant même d'essayer) ; le téléchargement depuis
Internet déclenche toujours l'avertissement SmartScreen sur le premier
lancement (clic droit → Propriétés → « Débloquer »). Ces deux limites
disparaîtront avec la signature Microsoft (voie B ci-dessous).

### Voie historique : le MSI fait tout (v2.0)

L'installateur **`RoomEq-<version>-win-x64.msi`** installe **tout en un
clic** — application, carte virtuelle, ASIO — sans aucune étape manuelle :

1. Double-cliquez sur le MSI et acceptez l'invite administrateur.
2. C'est tout. L'installateur enchaîne automatiquement :
   - import du **certificat de test** dans les magasins `Root` et
     `TrustedPublisher` (le paquet pilote est accepté sans confirmation) ;
   - activation du **mode test** de Windows (`bcdedit /set testsigning on`)
     — une invite propose le redémarrage à la fin de l'installation ;
   - ajout du pilote au **magasin Windows** (`pnputil /add-driver /install`) ;
   - **création du périphérique** `ROOT\ROOMEQVAD` (recette devcon/SetupAPI)
     et liaison du pilote.
3. Après le redémarrage (une seule fois, uniquement si le mode test vient
   d'être activé), les périphériques **« RoomEq Input »** et
   **« RoomEq Output »** apparaissent dans le panneau son.

La désinstallation (Panneau de configuration → Applications) retire tout
aussi proprement : périphérique, paquet de pilotes et certificat de test.

> **Secure Boot** : si le BIOS/UEFI a le Secure Boot actif, l'activation du
> mode test échoue — l'installateur s'arrête alors avec un message clair
> (rien n'est cassé : l'installation est annulée). Désactivez le Secure
> Boot pour utiliser un pilote signé-test, ou attendez la signature
> Microsoft (voie B). L'intégrité de la mémoire (HVCI) peut également
> bloquer les pilotes de test sur certaines machines : voyez le Gestionnaire
> de périphériques si la carte n'apparaît pas après le redémarrage.
>
> Un build **signé Microsoft** (voie B) ne déclenche ni certificat, ni mode
> test, ni redémarrage : l'installateur le détecte automatiquement
> (absence de `.cer` dans le paquet pilote).

### Si Windows bloque l'installation (dépannage express)

Le MSI v2.0 est **non signé** (pas encore d'Authenticode) : téléchargé
d'Internet, il déclenche les garde-fous Windows. Identification rapide :

| Message à l'écran | Cause | Action |
|---|---|---|
| « Windows a protégé votre ordinateur » (écran bleu) | SmartScreen + marque « du web » | Clic droit sur le MSI → Propriétés → cocher **« Débloquer »** → OK ; ou « Informations complémentaires » → « Exécuter quand même » |
| « Opération annulée » / fichier supprimé | Antivirus / Defender | Sécurité Windows → Historique de protection → **Restaurer** ; exclusion temporaire du fichier ; vérifier la taille (37 408 768 o) |
| « Modifications non autorisées bloquées » (msiexec.exe / SrTasks.exe) | **Accès contrôlé aux dossiers** (anti-ransomware) | Protection contre les ransomware → désactiver « Accès contrôlé aux dossiers » le temps de l'installation puis réactiver ; autoriser `msiexec.exe` seul **ne suffit pas** (SrTasks/bcdedit/pnputil bloqués aussi — cas 3bis du guide) |
| « Impossible d'activer le mode de signature de test… » (message RoomEq) | **Secure Boot actif** | Désactiver Secure Boot dans le BIOS/UEFI puis relancer (voir ci-dessous) |
| Code 1603 / « installation interrompue » | Échec en cours | Journal : `msiexec /i RoomEq-…msi /l*v "%TEMP%\RoomEq-install.log"` puis chercher `RoomEq CA ERREUR` |

Le guide complet (8 cas, procédures pas à pas, codes d'erreur) est publié
avec les livrables : **`TROUBLESHOOT-INSTALLATION.md`**, accompagné du
script **`Installer-RoomEq.ps1`** (débloque + installe + journal + lecture
de l'erreur, auto-élévation UAC).

### Depuis l'application (zip, sans MSI)

1. Ouvrez RoomEq → onglet **Mixage & routage**.
2. Vérifiez que le protocole « **Carte virtuelle RoomEq** » est sélectionné.
3. Cliquez **« Installer le pilote RoomEq… »** et acceptez l'invite
   administrateur. Le paquet (`roomeqvad.inf` / `.sys` / `.cat`) est installé
   par `pnputil` — **rien n'est téléchargé**.
4. Les périphériques **« RoomEq Input »** et **« RoomEq Output »** apparaissent
   dans le panneau son (Windows les groupe sous « RoomEq Virtual Audio »).

Si l'installation réussit mais que **les périphériques n'apparaissent pas**,
le pilote est probablement signé-test et bloqué : passez à la voie A
ci-dessous. Le bouton « Supprimer le pilote » retire proprement le paquet.

---

## Voie A — Signature de test (gratuit, machines de test)

> Le **MSI exécute déjà automatiquement** toutes les étapes ci-dessous :
> elles ne sont utiles que pour une installation manuelle (zip, pilote seul)
> ou pour comprendre ce que fait l'installateur.

La CI produit déjà des pilotes signés-test avec un certificat dédié ; le
fichier `RoomEq-TestSigning.cer` est fourni **à côté** du pilote dans les
artefacts de build. Sur la machine cible (session administrateur) :

```bat
:: 1) installer le certificat de test dans les magasins de confiance
certutil -addstore Root RoomEq-TestSigning.cer
certutil -addstore TrustedPublisher RoomEq-TestSigning.cer

:: 2) activer le mode test de Windows (redémarrage requis)
bcdedit /set testsigning on
shutdown /r /t 0

:: 3) après redémarrage : installer le pilote
pnputil /add-driver roomeqvad.inf /install /scan
```

Après le redémarrage, un filigrane « Mode de test » apparaît en bas à droite
de l'écran : il signale simplement que Windows accepte les pilotes signés-test.

**Retirer le mode test** :

```bat
bcdedit /deletevalue testsigning
shutdown /r /t 0
```

### Signer soi-même un pilote compilé localement

`driver\roomeqvad\build\sign_driver_test.ps1` fait tout automatiquement
(certificat local, signature du `.sys` et du `.cat`, export du `.cer`) :

```powershell
powershell -ExecutionPolicy Bypass -File driver\roomeqvad\build\build_driver.ps1 -TestSign
```

---

## Voie B — Attestation Microsoft (distribution officielle)

> La voie B est aussi **le préalable au Microsoft Store** et à l'objectif
> « Windows ne bloque rien d'office » (plus de mode test, certificat,
> redémarrage ni Secure Boot) : feuille de route complète des 3 phases
> (attestation → MSI signé → Store MSIX) dans **`docs/SIGNATURE-ET-STORE.md`**.

L'attestation (« attestation signing ») fait signer le pilote **par Microsoft**
après contrôle automatique : les utilisateurs n'ont ni mode test, ni
certificat à installer. Étapes :

1. **Certificat à signature EV** : achetez un certificat de signature de code
   EV (DigiCert, Sectigo, GlobalSign… ~300 €/an, carte à puce ou HSM cloud
   selon le fournisseur). Il doit être au nom de la personne/l'entité qui
   publiera le pilote.
2. **Compte développeur matériel** : créez un compte Microsoft (Azure AD) et
   inscrivez-vous au programme matériels (Partner Center → Hardware
   Dev Center). L'inscription requiert le certificat EV pour prouver
   l'identité.
3. **Préparer l'envoi** :
   ```powershell
   # le dossier du paquet (roomeqvad.sys + .inf + .cat signé EV)
   MakeCab /f RoomEq.ddf          # ou l'archive CAB attendue par le portail
   signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 ^
             /f certificat-ev.pfx /p <motdepasse> roomeqvad.cab
   ```
4. **Soumettre** sur le portail (Hardware Dev Center → New hardware
   submission → pilote « attestation »). Le contrôle automatique prend
   généralement moins de 24 h.
5. **Récupérer** le `.cat` attesté signé par Microsoft, remplacer celui du
   paquet, republier l'installateur : le pilote s'installe alors sur
   n'importe quel Windows 10/11 64 bits **sans mode test**.

> Limites connues : l'attestation ne permet pas les pilotes avec composants
> tiers non signés et les comptes individuels peuvent être soumis à revue
> manuelle. Le pilote RoomEq (déclaré UNIVERSAL, sans dépendance exotique)
> est conforme au socle d'attestation.

---

## 3. Détails techniques (référence)

- **Pilote** : `roomeqvad.sys` — WDM/PortCls WaveRT (miniport WaveRT +
  topologies), dérivé de l'exemple SYSVAD du WDK (MIT, cf.
  `THIRD_PARTY_NOTICES.md`). Une paire d'endpoints : rendu « RoomEq Input »
  (Windows y joue), capture « RoomEq Output » (l'application lit), pont
  interne render→capture (`bridge.cpp`).
- **Format** : 48 kHz, stéréo, float 32 bits — le moteur audio Windows
  rééchantillonne tous les flux vers ce format.
- **INF** : installation par énumération racine (`ROOT\ROOMEQVAD`), service
  `roomeqvad` à la demande, fichiers dans le magasin de pilotes.
- **Désinstallation** : `pnputil /delete-driver oemNN.inf /uninstall /force`
  (le bouton « Supprimer le pilote » et l'installateur MSI le font).
- **Compilation** : `driver\roomeqvad\build\build_driver.ps1` — Visual Studio
  2022 (charge C++) ; le WDK 10 arrive par NuGet au premier build
  (~500 Mo, aucune extension VS à installer).
