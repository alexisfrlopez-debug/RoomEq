# De la signature de test au Store — éliminer tous les blocages Windows

> Réponse structurée à l'objectif : **« proposer RoomEq sur le Microsoft
> Store, sans manipulations (PowerShell, déblocage, désactivation) et sans
> que Windows ne bloque rien d'office ».**
>
> Ce document fait le lien entre `INSTALLATION-PILOTE.md` (signature du
> pilote), `TROUBLESHOOT-INSTALLATION.md` (blocages actuels) et
> `docs/DISTRIBUTION.md` (monétisation/Store). Montants indicatifs
> (octobre 2026) — à revérifier sur les pages officielles.

---

## 1. La règle d'or : une seule condition racine

RoomEq embarque un **pilote noyau** (carte son virtuelle). Pour ce type
d'application, « Windows ne bloque rien d'office » se réduit à **une seule
condition** :

> **Le pilote doit être signé par Microsoft** (attestation ou WHQL).

Tous les blocages rencontrés jusqu'ici découlent de la **signature de
test** — le contournement gratuit et temporaire que la v2.0 utilise :

| Blocage | Cause racine | Disparaît avec |
|---|---|---|
| SmartScreen « Windows a protégé votre ordinateur » | MSI sans Authenticode + marque « du web » | MSI signé (phase B) / Store (phase C) |
| « Modifications non autorisées bloquées » (CFA) | Installateur non signé → heuristiques anti-ransomware | MSI signé avec réputation (phase B) |
| Certificat de test à importer + `bcdedit /set testsigning on` + **redémarrage** | Pilote signé-test | **Pilote attesté Microsoft (phase A)** |
| Secure Boot actif → installation refusée | Le mode test est incompatible Secure Boot | **Pilote attesté Microsoft (phase A)** |
| Filigrane « Mode de test » sur le bureau | Mode testsigning | **Pilote attesté Microsoft (phase A)** |
| HVCI qui peut bloquer le pilote de test | Pilotes de test soupçonnés par l'isolation du noyau | **Pilote attesté Microsoft (phase A)** |

Aucune astuce technique ne peut contourner cette règle : c'est le modèle
de sécurité de Windows qui fonctionne comme prévu. C'est aussi vrai pour
tous les concurrents (VB-CABLE, VoiceMeeter, Equalizer APO) : **leurs
pilotes sont signés Microsoft** — c'est le ticket d'entrée de la catégorie.

---

## 2. Ce que le Store change — et ne change PAS

| | Hors Store (site web, zip/MSI) | Microsoft Store |
|---|---|---|
| Signature du **paquet app** | À acheter soi-même (Authenticode) | **Fournie par Microsoft** (re-signée) |
| SmartScreen / CFA sur l'install | Disparaît avec un MSI signé (EV : immédiat ; OV : après réputation) | **N'existe pas** (installation gérée par le Store) |
| Mises à jour | Manuelles / winget | **Automatiques** |
| Découverte / confiance / avis / paiement | À construire soi-même | Inclus |
| **Le pilote noyau** | Installé par le MSI signé (elevation UAC, silencieuse avec pilote attesté) | **PAS installable par MSIX** — aucune exception |

Le point crucial : **un paquet MSIX ne peut pas installer de pilote noyau**
(runFullTrust donne les droits de l'utilisateur, pas ceux du noyau). Le
Store ne résout donc PAS le pilote — il résout l'app. La stratégie gagnante
combine les deux, comme le font les acteurs de l'audio (voir § 3).

---

## 3. Architecture cible (patron industriel éprouvé)

Le schéma utilisé par « Realtek Audio Console », « Waves MaxxAudio », les
consoles audio OEM — l'app UI sur le Store, le pilote livré par un canal
pilote légitime — adapté à un indépendant :

```
┌─ Microsoft Store ────────────────────────────────┐
│  RoomEq (MSIX, runFullTrust, signé Microsoft)    │
│  • installation 1 clic, sans UAC, sans avertissement
│  • mises à jour automatiques                      │
│  • embarque le paquet pilote ATTESTÉ en donnée    │
└──────────────────────┬───────────────────────────┘
                       │ premier lancement
                       ▼
   « Activer la carte son virtuelle » (1 bouton)
       → 1 invite UAC (inévitable : un pilote est une opération privilégiée)
       → pnputil (silencieux : pilote Microsoft-signé → aucun avertissement)
       → création du périphérique ROOT\ROOMEQVAD (recette devcon déjà
         implémentée dans installer/RoomEq.Installer.CA/InstallActions.cs
         — à porter côté app dans DriverSetupManager.cs)
       → cartes « RoomEq Input / Output » opérationnelles, SANS redémarrage
```

**UX finale : Store → Installer → Lancer → 1 clic + 1 UAC → ça marche.**
Aucune ligne de commande, aucun redémarrage, aucun avertissement. La
seule invite restante (UAC à l'activation du pilote) est une constante de
Windows pour toute opération pilote — les applications signées du marché
l'ont aussi.

**Cas ASIO** : la DLL ASIO s'enregistre par COM
(`CLSID\{8F5E3A91-…}\InprocServer32`, cf. `installer/RoomEq.wxs`). Deux
options, à trancher pendant la soumission test (flight) :
- **Option 1 — extension `com:ComServer` du manifeste MSIX** : enregistre
  la classe au niveau système à l'installation du paquet, propre et
  déclaratif ; à valider avec un DAW réel (la DLL vit dans WindowsApps,
  les DAW doivent pouvoir la charger — c'est le mécanisme prévu pour ça).
- **Option 2 — utilitaire d'activation du premier lancement** : le même
  helper élevé qui installe le pilote écrit la clé HKLM et copie la DLL
  vers un emplacement stable (Program Files) ; fonctionne à coup sûr,
  un cran moins « Store-idiomatique ».

---

## 4. Feuille de route en trois phases

| Phase | Contenu | Coût | UX obtenue |
|---|---|---|---|
| **A. Pilote attesté** | Certificat code signing **EV** (DigiCert/Sectigo/SSL.com, ~300–500 €/an) → inscription **Hardware Dev Center** (compte Partner Center hardware, gratuit après vérification d'identité) → soumission du paquet `roomeqvad` en **attestation signing** (gratuit, contrôle automatique < 24 h) → récupération du `.cat` signé Microsoft | ~300–500 €/an | Le pilote s'installe **silencieusement** sur tout Windows 10/11 x64 : plus de mode test, plus de certificat à importer, plus de redémarrage, plus de souci Secure Boot/HVCI |
| **B. MSI signé** | Le **même certificat EV** signe le MSI (`signtool`) — build CI : étape signature après `build_msi.ps1` ; l'installateur détecte déjà l'absence de `.cer` et saute mode test/reboot | 0 € (certificat de la phase A) | Téléchargement web : double-clic → UAC → **c'est tout**. SmartScreen et CFA ne bloquent plus un binaire EV-signé |
| **C. Store MSIX** | Compte développeur Partner Center (**19 $ une fois**, individuel) → remplacer l'identité placeholder du manifeste (`installer/msix/Package.appxmanifest`) → porter l'activation pilote au premier lancement (§ 3) → décision ASIO (§ 3) → mise en conformité fichiers `%LOCALAPPDATA%` (déjà documentée dans `docs/DISTRIBUTION.md` § 2.2) → soumission flight → revue → publication | 19 $ + le travail d'ingénierie ci-contre | Store : 1 clic. Voir § 3 — UX finale |

**Ordre incompressible : A avant B avant C.** La phase A est la seule qui
dépende d'un achat et d'entités externes (autorités de certification,
Microsoft) — tout le reste est du code et de l'outillage que le projet
contrôle.

### Avancement du chantier (ce qui est déjà prêt)

- ✅ **Scaffolding MSIX** (v2.1.0) : `installer/msix/Package.appxmanifest`
  (identité placeholder documentée, `runFullTrust`, logos Store générés),
  `installer/msix/build_msix.ps1` (publish + payloads + makeappx), job CI
  **msix** dans `.github/workflows/build.yml` (paquet non signé, artefact
  par build ; la soumission Store est re-signée par Microsoft).
- ✅ **Chaîne pilote/ASIO CI** : artefact `roomeq-driver-<sha>` réutilisé
  tel quel par le job MSIX.
- ✅ **Recette de création du périphérique** : écrite et éprouvée dans
  `InstallActions.cs` (devcon : `SetupDiCreateDeviceInfo` +
  `DIF_REGISTERDEVICE` + `UpdateDriverForPlugAndPlayDevices`) — à porter
  dans `src/RoomEq.Audio/Routing/DriverSetupManager.cs` pour le premier
  lancement.
- ⬜ Phase A : achat certificat EV + inscription Hardware Dev Center +
  soumission attestation (dépend de vous).
- ⬜ Phase B : étape `signtool sign` dans `build_msi.ps1` + secrets CI
  (certificat en base64, protégé par mot de passe dans GitHub Secrets).
- ⬜ Phase C : identité Partner Center dans le manifeste, portage du
  premier lancement, décision ASIO (com:ComServer vs helper), migration
  d'écriture `%LOCALAPPDATA%`, politique de confidentialité, soumission.

---

## 5. Décisions à prendre (côté projet)

1. **Budget phase A** (~300–500 €/an) : c'est le seul vrai coût récurrent
   et la condition de tout le reste. Sans lui, RoomEq reste « signé-test »
   et les manipulations de `TROUBLESHOOT-INSTALLATION.md` restent le prix
   d'entrée (comme pour les builds de test d'Equalizer APO aujourd'hui).
2. **Statut juridique du compte** : Partner Center individuel (19 $) ou
   entreprise (99 $) — voir `docs/DISTRIBUTION.md` § 6 (la vente suppose
   un statut, la distribution gratuite non).
3. **Modèle** : gratuit au lancement (maximiser les avis et la base
   d'utilisateurs) puis achat unique 5–15 € — recommandation déjà actée
   dans `docs/DISTRIBUTION.md` § 1, réversible à tout moment.
4. **Prio v2.1.0** : ce chantier Store vs. autres demandes — le
   scaffolding CI est fusionné et non bloquant ; le portage du premier
   lancement (~1 journée) ne devient utile qu'après la phase A.

---

## 6. En attendant (v2.0.0 signée-test, distribution de test)

Pour tester **aujourd'hui**, les blocages restent ceux de
`TROUBLESHOOT-INSTALLATION.md` — dans l'ordre : Débloquer (SmartScreen),
CFA off pendant l'installation, Secure Boot off dans le BIOS. La
désinstallation puis réinstallation après la phase A bénéficiera
automatiquement du mode « pilote attesté » : l'installateur détecte
l'absence de `.cer` dans le paquet pilote et saute certificat/mode
test/redémarrage sans aucune modification de code (déjà implémenté).

## 7. « Peut-on charger le pilote sans signature ET sans mode test ? »

Question légitime (oct. 2026) : *« il existe bien des drivers non signés qui
arrivent à s'installer tout de même normalement, non ? »* Réponse honnête,
en trois parties.

### 7.1 Ce que l'on observe n'est pas ce que l'on croit

Les « pilotes non signés qui s'installent » sont en réalité, dans quasi tous
les cas :

| Observation | Réalité technique |
|---|---|
| « Ce pilote tiers n'est pas signé et pourtant il charge » | Il **est** signé — par Microsoft (**attestation signing**, invisible : le certificat du vendeur ne s'affiche nulle part dans les propriétés du fichier). C'est la voie de la phase A. |
| « Celui-là s'installe sans problème » | **Installer ≠ charger.** `pnputil` ajoute volontiers un paquet non signé au magasin ; le blocage a lieu au **chargement noyau** (périphérique code 52). C'est exactement le symptôme « installé mais carte absente » de RoomEq. |
| « Les vieux pilotes non-MS chargent encore » | Signés en **cross-signing** avant le 29 juillet 2015 — pratique que Microsoft a supprimée depuis ; ces pilotes bénéficient d'un sursis (et sont bloqués si HVCI est actif). |
| « Ce périphérique fonctionne sans pilote signé » | Pilote **user-mode** (UMDF) ou classe Windows intégrée — pas un pilote noyau. |

Une carte son virtuelle exige un pilote **noyau** (architecture audio
Windows) : aucun contournement user-mode n'existe.

### 7.2 Les « astuces » qui existent — et pourquoi les écarter

| Piste | Verdict |
|---|---|
| **F7 « Désactiver l'application obligatoire des signatures de pilotes »** (Paramètres → Système → Récupération → Démarrage avancé → Paramètres de démarrage → touche 7) | **Fonctionne, 100 % graphique** — mais **effacée au premier redémarrage** : bon pour un test d'une session, inutilisable au quotidien (la carte disparaît à chaque reboot). Exige aussi Secure Boot désactivé. |
| `bcdedit /set nointegritychecks on` | Pire que le mode test (désactive TOUT contrôle), ignoré avec Secure Boot, partiellement supprimé des builds récents — et ligne de commande + redémarrage. Aucun intérêt. |
| Outils type DSEFix / EfiGuard / kdmapper (bypass noyau) | Techniques de **malware** : Defender les met en quarantaine à l'instant, PatchGuard provoque des BSOD aléatoires, chaque mise à jour Windows les casse. Jamais, même pour un test perso : la machine devient instable ET l'antivirus combat l'installateur. |
| « Spoofer » la signature (modifier le `.cat` / le certificat) | Toute modification du fichier ** invalide** la signature ; un certificat maison ne passe la validation racine Microsoft **que** justement en mode test. Cercle complet : le mode test est déjà la version officielle de cette idée. |

### 7.3 Conclusion pratique

- **Sans mode test et sans signature Microsoft : le pilote noyau ne peut pas
  charger. Point.** Ce n'est pas un manque d'ingénierie côté RoomEq, c'est le
  modèle de sécurité de Windows x64 (conception).
- Pour **tester** : le mode test (une case dans la fenêtre graphique
  `RoomEqDriverSetup`, un redémarrage, un filigrane réversible via
  « Revenir au mode normal »), ou l'application **sans carte virtuelle**
  (lecteur, EQ, calibration, boucle WASAPI fonctionnent).
- Pour **distribuer proprement** : phase A (attestation Microsoft) — c'est
  précisément la raison d'être de la feuille de route ci-dessus. Le jour où
  le paquet pilote revient signé Microsoft, l'installateur saute tout seul
  certificat/mode test/redémarrage (déjà implémenté).
