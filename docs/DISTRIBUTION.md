# Guide de distribution et de monétisation — RoomEq

> Feuille de route pour publier RoomEq (Windows Store et au-delà) et en tirer
> un revenu, sous licence **MIT** (`LICENSE`). Particularité du modèle : MIT
> autorise chacun à utiliser, modifier et redistribuer — même en vendant — la
> monétisation repose donc sur la **commodité et le service** (version Store
> signée, mises à jour automatiques, support, packaging), pas sur
> l'exclusivité du code. Les montants et pourcentages cités sont indicatifs
> (octobre 2026) : à revérifier sur les pages officielles avant toute décision.

---

## Sommaire

1. [Positionnement et modèles de monétisation](#1-positionnement-et-modèles-de-monétisation)
2. [Windows Store (Microsoft Store)](#2-windows-store-microsoft-store)
3. [Vente directe hors Store](#3-vente-directe-hors-store)
4. [winget : distribution gratuite de découverte](#4-winget--distribution-gratuite-de-découverte)
5. [Autres plateformes : ce que cela implique vraiment](#5-autres-plateformes--ce-que-cela-implique-vraiment)
6. [Aspects légaux et administratifs](#6-aspects-légaux-et-administratifs)
7. [Ordre recommandé et checklist](#7-ordre-recommandé-et-checklist)

---

## 1. Positionnement et modèles de monétisation

RoomEq occupe une niche réelle et mal servie : la **correction acoustique de
pièce grand public sous Windows**, combinée à un **routage complet du son du
système vers l'égaliseur** (VB-CABLE/VoiceMeeter automatiques), un **analyseur
temps réel** et une **bibliothèque de 36 profils de micros de mesure**. Les
alternatives existantes (REW gratuit mais complexe, Equalizer APO gratuit mais
technique, chaînes matérielles coûteuses type Dirac Live à 100–300 €) laissent
un espace pour un produit simple, en français, portable et sans compte.

Trois modèles de revenus se combinent bien avec la licence MIT :

| Modèle | Principe | Avantages | Limites |
|---|---|---|---|
| **Achat unique** (Store et/ou direct) | Prix fixe une fois, mises à jour incluses — le client paie la commodité (signature, mises à jour auto, confiance) | Simple à comprendre, pas d'infrastructure | Revenu ponctuel, nécessite du volume |
| **Gratuit + version Pro** | Version « découverte » gratuite, calibration avancée/presets payants | Tunnel d'adoption naturel, essai sans risque | Deux binaires à maintenir (l'infrastructure test/public existe déjà !) |
| **Donations / Sponsors** | App gratuite, dons (GitHub Sponsors, Ko-fi) | Zéro friction, bonne image, compatible MIT | Revenu aléatoire |

Avec MIT, n'importe qui peut recompiler et donner le logiciel : le prix se
justifie par ce que la redistribution gratuite n'offre pas — signature,
confiance du Store, mises à jour automatiques, support et suivi. C'est le
modèle éprouvé des logiciels open source à offre payante (7-Zip/WinRAR vs
Store, etc.).

Recommandation pragmatique : commencer par un **achat unique à petit prix**
(5–15 €) sur le Store — palier d'entrée — puis ajuster selon les retours. Le
mécanisme `AppFlavor` (test/public) du projet fournit déjà la séparation
technique pour un futur modèle « gratuit/Pro ».

---

## 2. Windows Store (Microsoft Store)

Le Store est le canal le plus cohérent pour RoomEq : paiement géré, confiance
de l'utilisateur (antivirus, avis), mises à jour automatiques, visibilité.

### 2.1 Prérequis

- **Compte Microsoft Partner Center** : inscription individuelle ≈ **19 $ USD
  (une fois)**, ou 99 $ pour une entreprise. Vérification d'identité obligatoire.
- **Packaging MSIX** de l'application : RoomEq est une app .NET 8 Avalonia
  auto-contenue `win-x64` — le même `publish` que le packaging actuel sert de
  base ; il faut ajouter un projet de packaging (Projet WAP ou `dotnet msix`,
  manifeste `Package.appxmanifest`, icônes aux tailles Store).
- **Capacité `runFullTrust`** : indispensable ici — RoomEq manipule les
  périphériques audio WASAPI, les interfaces COM (bascule des périphériques
  par défaut, contrôle du volume de point de terminaison) et pilote ASIO.
  C'est un cas standard pour les apps audio de bureau ; elle est accordée dès
  lors que l'app en justifie dans la soumission.
- **Certificat de signature** : en Store, la signature Microsoft s'applique ;
  pour les tests locaux, un certificat auto-signé suffit.

### 2.2 Points d'attention spécifiques à RoomEq

| Sujet | Situation actuelle | Action avant soumission |
|---|---|---|
| **Pilote noyau** (carte virtuelle) | Signé-test : mode test + redémarrage + Secure Boot off — et un paquet MSIX **ne peut pas** installer de pilote | **Phase A (préalable absolu) : attestation Microsoft** du pilote, puis activation au premier lancement (1 UAC, recette déjà dans `InstallActions.cs`) — feuille de route complète : **`docs/SIGNATURE-ET-STORE.md`** |
| ASIO (COM `InprocServer32`) | Enregistré par le MSI dans HKLM | Extension `com:ComServer` du manifeste MSIX ou helper d'activation — décision à trancher en soumission test (voir `SIGNATURE-ET-STORE.md` § 3) |
| `IPolicyConfig` (API non documentée) | Utilisée pour basculer les périphériques par défaut | Risque de rejet/évolution de Windows : documenter le repli (panneau son) déjà prévu dans le code ; l'écrire explicitement dans la revue |
| Installation VB-CABLE/VoiceMeeter | Téléchargement depuis `download.vb-audio.com` + UAC | Fonctionne en full trust ; mais le Store n'aime pas les téléchargements externes — prévoir un mode « sans pilote externe » (loopback WASAPI, déjà implémenté) comme parcours par défaut dans la fiche |
| Télémétrie | La version publique n'envoie **rien** (garde-fou `AppFlavor`) | Argument conformité fort ; fournir une politique de confidentialité simple (« aucune donnée collectée, aucun envoi réseau ») |
| Fichiers en écriture | presets/`mixer.json` à côté de l'exe (portable) | En MSIX, le dossier d'installation est en lecture : basculer l'écriture vers `%LOCALAPPDATA%\RoomEq` (repli déjà prévu pour `telemetrie.json`) — chantier de mise en conformité principal |
| Déclaration système | `runFullTrust`, pas de service | Rien à faire ; l'anti-larsen et la restauration des périphériques jouent en faveur de la revue |

### 2.3 Économie du Store

- Commission Microsoft : **≈ 15 %** (reversés ≈ 85 % au développeur ; ≈ 95 %
  quand le client paie via un moyen de paiement lié à son compte Microsoft).
- Essai gratuit : possible (période d'essai native) — compatible avec le
  modèle « achat unique ».
- Mises à jour : publiées via Partner Center, déployées automatiquement.

### 2.4 Étapes concrètes

1. **Pilote attesté Microsoft** (phase A de `docs/SIGNATURE-ET-STORE.md` — certificat EV ~300–500 €/an) : sans ça, le Store est impossible pour l'expérience complète.
2. Créer le compte Partner Center (individuel) et payer les 19 $.
3. Réserver le nom d'application (« RoomEq »).
4. Remplacer l'identité placeholder de `installer/msix/Package.appxmanifest`, compléter le job CI `msix` (scaffolding déjà fusionné).
5. Porter l'activation pilote au premier lancement (`DriverSetupManager.cs` + recette devcon d'`InstallActions.cs`), trancher ASIO (com:ComServer vs helper).
6. Adapter l'écriture des fichiers vers `%LOCALAPPDATA%` (§ 2.2).
7. Soumettre une version de test (flight) → revue Microsoft (quelques jours).
8. Publier en achat unique à prix d'entrée, surveiller les avis, itérer.

---

## 3. Vente directe hors Store

Complément idéal au Store (commission réduite, relation client directe) :

| Plateforme | Commission indicative | Points forts |
|---|---|---|
| **Lemon Squeezy** | ≈ 5 % + 0,50 $ | « Merchant of record » : TVA/volume d'affaires gérés pour vous, clés de licence intégrées |
| **Paddle** | ≈ 5 % + 0,50 $ | Idem, orienté logiciel, plus corporate |
| **Gumroad** | ≈ 10 % | Mise en ligne en minutes, simple, audience découverte |
| **itch.io** | 0–20 % (au choix) | Communauté indie, choix du pourcentage vous-même |

Formule recommandée : vendre le **zip portable** (déjà produit par
`package_1.8.1.sh`) avec mises à jour à vie, **sans DRM** — la niche est de
confiance, et sous MIT le client paie la commodité et le soutien au projet,
pas une exclusivité. Prévoir une page produit simple (captures, FAQ reprise
du README) ; conserver la mention de copyright et le lien vers la licence
sur la page et dans les livrables.

---

## 4. winget : distribution gratuite de découverte

Publier RoomEq dans **winget** (gestionnaire de paquets Windows) est gratuit et
augmente la découvrabilité des utilisateurs techniques : soumission d'un
manifeste au dépôt `microsoft/winget-pkgs` (nom d'éditeur, version, SHA-256 du
zip, URL de téléchargement publique stable — hébergement GitHub Releases du
dépôt officiel). Mise à jour : un nouveau manifeste par version. Pas de
paiement : c'est un canal d'acquisition qui renvoie vers le Store/la vente
directe.

---

## 5. Autres plateformes : ce que cela implique vraiment

Avalonia rend l'**interface** multiplateforme, mais attention au cœur audio :

- **RoomEq.Audio repose sur NAudio** (WASAPI, ASIO, MMDevice) : la couche
  Windows est profonde (COM, `IAudioEndpointVolume`, `IPolicyConfig`,
  VirtualAudioInstaller). Un portage exige une couche d'abstraction audio
  (ALSA/PulseAudio/PipeWire sous Linux, CoreAudio sous macOS) — un chantier
  comparable à la v1.x entière, à ne pas sous-estimer.
- **macOS** : notarisation obligatoire (compte Apple Developer ≈ 99 $/an),
  binaires universels, quittance Gatekeeper — uniquement après un portage
  audio complet.
- **Linux** : pas de Store dominant ; AppImage/Flatpak (Flathub) gratuits.
  Le marché du correction acoustique Linux est minuscule mais fidèle.
- **Conclusion** : rester Windows-first (Store + direct) ; envisager Linux/
  macOS seulement si le produit le justifie économiquement, après une
  refactorisation propre de `RoomEq.Audio` derrière une interface portable.

---

## 6. Aspects légaux et administratifs

- **Licence** : MIT pour le code (attribution conservée). Pour le Store,
  Microsoft applique son contrat standard de développeur ; pour la vente
  directe, un EULA court peut compléter (garanties, support) sans contredire
  la MIT sur les droits d'usage du logiciel.
- **Politique de confidentialité** : une page simple suffit — la version
  publique ne collecte rien et n'envoie rien (argument différenciant à
  afficher sur la fiche Store).
- **Revenus** : en France, l'encaissement suppose un statut (micro-entreprise
  BNC/BIC ou équivalent) ; Lemon Squeezy/Paddle agissent comme marchands de
  titre et simplifient la TVA internationale. À valider avec un
  comptable/conseiller — hors périmètre de ce document.
- **Marque** : vérifier la disponibilité du nom « RoomEq » (INPI/eUIPO,
  recherches web) avant toute mise en avant commerciale sérieuse.

---

## 7. Ordre recommandé et checklist

**Séquence conseillée** : (1) GitHub Releases propres par version (hébergement
stable des zips) → (2) winget → (3) vente directe Gumroad/Lemon Squeezy du zip
→ (4) Windows Store avec le packaging MSIX → (5) décider du portage selon les
revenus.

Checklist minimale avant la première vente :

- [ ] Releases GitHub v1.8.1+ (zips test/public + notes de version)
- [ ] `THIRD_PARTY_NOTICES.md` embarqué dans les zips (obligation des licences MIT)
- [ ] Page produit (captures, FAQ, tarifs) + politique de confidentialité
- [ ] Écriture des fichiers conforme MSIX (`%LOCALAPPDATA%`)
- [ ] Compte Partner Center créé, nom réservé
- [ ] Prix fixé, pages Store rédigées en français (et anglais si ciblage élargi)
- [ ] Processus de mise à jour répétible (script de packaging + soumission)
