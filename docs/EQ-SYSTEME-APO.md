# EQ système sans pilote — contourner l'exigence de signature Microsoft

- **Session** : 19 (1er novembre 2026) · **Statut** : **DÉCISION D24 ACTÉE**
  (2 novembre 2026) — vague 9 LIVRÉE en v3.7.0 : `natif/roomeq-apo/`
  (harnais 112 vérifications), `src/RoomEq.Audio/SystemApo/` (23 tests
  xUnit), panneau « EQ système » dans l'onglet Mixage, DLL x64 + arm64
  déployées par l'installateur (spec as-built I-AUD-09 v1.1).
  **v3.9.1 (session 23, 2 novembre 2026)** : correctif activation sur les
  sorties dont le pilote ne crée pas la sous-clé `FxProperties` (premier
  retour terrain : « Ce périphérique n'expose pas de slot d'effets
  système » sur « Haut-parleurs (Realtek(R) Audio) » — pilote générique
  Windows). Le store CRÉE la sous-clé manquante à l'écriture (mécanisme
  standalone d'Equalizer APO documenté § 3.2 ; « if the FxProperties key
  is absent entirely, then no system effect APOs are used », É. Dechamps
  — donc une clé créée EST lue par le moteur) ; la sauvegarde mémorise
  l'existence d'origine (`FxPropertiesExisted`) et la désactivation
  supprime la clé créée si redevenue vide ; vue registre Registry64
  explicite (anti-WOW64). Spec I-AUD-09 v1.2, registrar 16 → 20 tests.
  **v3.9.2 (session 24, 2 novembre 2026)** : correctif activation (second
  retour terrain : « Requested registry access is not allowed » sur
  « Casque (JBL Tune Flex 2) »). FAIT ÉTABLI par le code source même
  d'Equalizer APO (`helpers/RegistryHelper.cpp` : le `createKey`
  FxProperties est enveloppé d'un `catch` « Permissions were not
  sufficient » → `takeOwnership` + `makeWritable` → retry) : les clés
  d'endpoints MMDevices appartiennent à SYSTEM et leur DACL n'accorde
  PAS l'écriture aux administrateurs — même élevé. Portage C# fidèle
  (`ApoRegistrySecurity.cs`) : SeTakeOwnershipPrivilege → owner = groupe
  Administrateurs (RegSetKeySecurity OWNER seul — le DACL n'est jamais
  touché par cette étape), puis ACE FullControl héritée (WRITE_DAC,
  droit implicite de l'owner), puis réessai ; la SÉCURITÉ d'origine
  (owner + DACL, descripteur binaire) est capturée AVANT la première
  écriture (`CaptureSecurity`, stockée dans la sauvegarde) et restituée
  à la désactivation (`RestoreSecurity`, SeRestorePrivilege — après la
  restauration des valeurs, avant l'effacement de la sauvegarde).
  Spéc I-AUD-09 v1.3, registrar 20 → 25 tests.
- **Question posée** : « Je voudrais que l'application installe son propre
  driver de carte son virtuelle afin de ne plus dépendre de VB-Cable, mais je
  bute sur le problème du driver signé Microsoft qui demande une solution
  payante. Je veux un contournement user friendly, sans mode test. Pourquoi
  ce problème alors qu'il existe des drivers non signés installables ? »

---

## 1. Où nous en sommes réellement

RoomEq v3 possède **déjà** trois chemins pour faire passer du son par l'EQ —
le problème n'est pas l'absence de pilote intégré, c'est sa **signature** :

| Chemin | Implémentation | Verdict |
|---|---|---|
| Carte virtuelle RoomEq | `roomeqvad.sys` (WDM/PortCls), bloc scellé v2.2.2, livré par l'installateur à côté de l'application (`VirtualAudioProviders.cs`, `DriverSetupManager.cs`) | **Signé test uniquement** → exige le mode test Windows (`bcdedit /set testsigning on`) et Secure Boot désactivé ; contrat de consentement obligatoire (spec `I-INS-05` : défaut NON, jamais de bascule silencieuse). Filigrane « mode test » sur le bureau. C'est ce que vous voulez supprimer. |
| Boucle WASAPI | `LoopbackProvider` — sans pilote | Fonctionne sans rien installer, mais la sortie captée doit différer de la sortie master (anti-larsen, D18) : ne couvre que la sortie captée. |
| ASIO intégré | `RoomEqAsio64.dll` — DLL **user-mode** | Aucun problème de signature (l'ASIO n'est pas un pilote noyau), mais seuls les DAW qui choisissent cet ASIO passent dedans. |
| VB-CABLE (externe) | Pilote tiers gratuit | Ce que l'utilisateur installe **en pratique** pour remplacer le pilote signé-test → dépendance tierce, exactement ce qu'on veut supprimer. |

Le mur à franchir : faire charger `roomeqvad.sys` sur une machine Windows
normale (Secure Boot actif, mode normal).

## 2. Pourquoi ce mur existe — et pourquoi des « drivers non signés » s'installent

### 2.1 La règle (politique Microsoft, pas un bug)

1. **Tout code noyau x64 doit être signé pour être chargé** — « The
   kernel-mode code signing policy requires that all kernel-mode code have a
   digital signature to load on 64-bit versions of Windows » (Microsoft Learn,
   *Loading Test Signed Code* ; idem *Test Signing*).
2. Sur une machine moderne (**Secure Boot actif**, configuration par défaut de
   tout UEFI récent), la signature exigée est **une signature Microsoft**
   (WHQL ou attestation) : les anciens certificats « cross-signing » des
   éditeurs ne suffisent plus depuis Windows 10 1607.
3. Un pilote **signé test** ne se charge qu'en **mode test** (filigrane de
   bureau, machine qui accepte ensuite n'importe quel pilote signé test —
   régression de sécurité que `I-INS-05` impose de refuser par défaut) — ou
   Secure Boot désactivé. C'est exactement la situation actuelle de
   `roomeqvad.sys`.

### 2.2 Le mur payant — et ce qu'il coûte vraiment en 2026

Signer un pilote soi-même passe par le **Hardware Dev Center** de Microsoft :

- créer le compte : « You must have an extended validation (EV) code signing
  certificate » (*Register for the Hardware Developer Program*) ;
- soumettre à l'attestation : « your Hardware Dev Center dashboard account
  must have at least one EV certificate associated with it » (*Driver Code
  Signing Requirements*).
- L'attestation elle-même est **gratuite** (et suffit pour un périphérique
  audio virtuel logiciel : pas de tests HLK matériel). Le payant, c'est le
  **certificat EV** chez une autorité de certification, avec vérification
  légale de l'entité et clé sur matériel dédié (obligatoire depuis juin
  2023). Prix constatés en octobre 2026 : **349–669 $/an** en CA directes
  (SSL.com, GlobalSign, Certum), **≈ 226–299 $/an** via revendeurs (Certum
  EV cloud). Récurrent, chaque année.

Il n'existe **aucune voie gratuite et légale** pour signer un pilote noyau.

### 2.3 Démêlage du paradoxe : « il existe des drivers non signés installables »

Les drivers que vous voyez s'installer « sans signature » tombent dans l'un de
ces cinq cas — **aucun n'est un driver noyau non signé au sens strict** :

1. **Ils sont en réalité signés Microsoft, sans que ça saute aux yeux.**
   VB-CABLE, Voicemeeter, LoopBeAudio… s'installent sur Windows 10/11 avec
   Secure Boot actif sans mode test → leur paquet pilote est **forcément**
   signé Microsoft (attestation/WHQL) — c'est une conséquence directe de la
   règle § 2.1. La signature vit dans le fichier `.cat` du paquet, invisible
   dans le panneau son : « non signé » apparent = « signé par l'éditeur et
   validé Microsoft ». **VB-CABLE n'est pas un driver non signé : c'est un
   driver dont l'éditeur a payé/obtenu la signature et le donne gratuitement.**
2. **Ce ne sont pas des pilotes noyau.** Les pilotes *user-mode* (UMDF), les
   DLL ASIO, les APO audio : du code utilisateur, pour lequel la signature
   Microsoft n'est **pas exigée** (au pire une signature CA classique, pour la
   réputation SmartScreen — pas pour fonctionner).
3. **Ils utilisent un pilote de classe Microsoft déjà signé** (WinUSB,
   usbser…) via un INF qui ne fait que le référencer : aucun nouveau code
   noyau à signer.
4. **Vieilles machines / Secure Boot désactivé** : le cross-signing
   historique y est encore toléré — cas en voie d'extinction.
5. **Machines en mode test** — le contournement que nous refusons (§ 1).

Autrement dit : le « driver non signé installable » est un mythe de
perception. Le pilote audio virtuel gratuit et fonctionnel que vous citez
(VB-CABLE) est exactement dans le cas 1 — il a franchi le mur payant à votre
place et vous en fait profiter gratuitement.

## 3. L'option recommandée : l'APO (Audio Processing Object) — le contournement légitime et gratuit

### 3.1 Renverser le problème

Toute l'architecture « câble virtuel » existe pour **amener le son du système
jusqu'à l'application** (Windows joue dans un render virtuel, l'app le capte,
l'EQ le traite, le rend sur la vraie sortie). L'APO fait l'inverse :
**installer le DSP de RoomEq à l'intérieur du moteur audio de Windows**
lui-même, au niveau du périphérique de sortie.

Un **APO** est un objet COM (DLL user-mode) que Windows charge dans
`audiodg.exe` — le processus qui mixe déjà tout le son du système. Il n'y a
donc **rien** à faire transiter, plus de câble, plus de bascule de périphérique
par défaut, plus de double conversion, **latence ajoutée nulle**.

Conséquences immédiates :
- **zéro pilote noyau → zéro signature Microsoft → zéro mode test → zéro
  Secure Boot → zéro dépendance tierce** ;
- installation = une invite administrateur (UAC) unique, immédiate,
  réversible — exactement le niveau d'effort de VB-CABLE, mais avec NOTRE
  DSP ;
- l'onglet Égaliseur existant pilote la courbe en temps réel ; la sortie
  reste le vrai périphérique Windows (volume système, mixer, tout reste
  natif).

### 3.2 Le mécanisme, documenté et éprouvé à grande échelle

- Les APO sont le mécanisme **officiel** d'effets audio de Windows («
  packaging their DSP algorithms as APOs and inserting them into the audio
  engine's signal processing graph », Microsoft Learn, *Audio Processing
  Object Architecture*). Les « améliorations » Realtek/Dolby de votre panneau
  son sont des APO.
- L'installation **standalone** (sans paquet pilote) se fait par le registre :
  sous
  `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\{GUID-du-endpoint}\FxProperties`,
  les valeurs identifiées `{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},5 / ,6 / ,7`
  (SFX / MFX / **EFX**) désignent le CLSID de l'objet COM à instancier
  (mécanisme documenté en détail par Étienne Dechamps, projet APO — et par le
  Configurator d'Equalizer APO).
- Position **EFX** (endpoint) : « applied to all streams that use the same
  endpoint. An endpoint effect is **always applied, even to raw streams** »
  (Microsoft Learn) — c'est la position que RoomEq utilisera.
- `audiodg.exe` tourne sous un compte de type LocalService **sans accès aux
  dossiers utilisateurs** : la configuration de l'APO devra vivre sous
  `%ProgramData%\RoomEq\apo\` (et pas dans %APPDATA%).
- **Aucune signature n'est exigée** pour qu'une APO tierce soit chargée
  (user-mode). Preuve à l'échelle : **Equalizer APO** — égaliseur système
  gratuit open source, binaires non signés (« File not signed, but legitimate
  audio equalizer software »), utilisé par des millions de machines Windows
  10/11 y compris 24H2 ; l'écosystème (Peace, HeSuVi, packs APO communautaires
  — encore actifs en janvier 2026) vit entièrement de ce mécanisme.

Point d'honnêteté : le chemin « APO standalone par registre direct » est
éprouvé mais **n'est pas une API officiellement supportée** par Microsoft
(les éditeurs de matériel passent par l'INF de leur driver ; Equalizer APO
qualifie sa propre installation de « basically a kind of hack which may
fail » sur certains périphériques). C'est le prix du gratuit : il faut
prévoir la **réparation automatique** (cf. § 3.4) et tester sur les
machines cibles. Depuis 10+ ans et des versions entières de Windows, ce
mécanisme n'a jamais été verrouillé par Microsoft.

**Fait établi v3.9.2 (permissions du registre MMDevices)** : les clés
d'endpoints sont créées par le service audio (SYSTEM) et leur DACL
n'accorde PAS l'écriture aux administrateurs — la preuve vit dans le code
d'Equalizer APO lui-même (`DeviceAPOInfo.cpp` : `createKey(FxProperties)`
est enveloppé d'un `catch (RegistryException) { takeOwnership;
makeWritable; createKey; }`, commentaire d'origine « Permissions were not
sufficient, so change them »). RoomEq fait exactement pareil (prise de
possession pour les Administrateurs + ACE FullControl héritée), À UNE
DIFFÉRENCE PRÈS, exigée par notre invariant de réversibilité : Equalizer
APO ne restitue JAMAIS owner/DACL d'origine — RoomEq les capture avant la
première écriture et les restitue à la désactivation (SeRestorePrivilege).

### 3.3 Ce que ça devient dans RoomEq

```
Aujourd'hui (câble virtuel)                     Cible (APO)
─────────────────────────────────────           ─────────────────────────────────
Windows ──► RoomEq Input (pilote.*)             Windows ──► audiodg.exe
              │                                  (moteur audio natif)
              ▼                                     │ mixe, convertit
         RoomEq Output (capture)                     ▼
              │                                RoomEqApo.dll (EFX) ◄── courbe
              ▼                                     │   %ProgramData%\RoomEq\apo\
         RoomEq app (resample + EQ)                  ▼
              │                                   vraie sortie (enceintes)
              ▼                                  
         vraie sortie                            Application = télécommande :
*.sys signé test → mode test                     écrit la courbe, choisit les
                                                 sorties à traiter — plus de
                                                 routage audio dans l'app.
```

- **Nouveau mode « EQ système (APO) »** dans l'onglet Mixage : liste des
  sorties Windows avec état (activée / non), boutons Activer/Désactiver par
  sortie, tout est réversible.
- L'application écrit la courbe de l'onglet Égaliseur (déjà synchronisée en
  temps réel) dans `%ProgramData%\RoomEq\apo\active.json` ; l'APO la recharge
  en arrière-plan (poll ~100 ms) et swap sa chaîne de filtres de façon
  atomique — **aucun son ne s'interrompt**.
- Le **cœur DSP est déjà prêt** : `natif/roomeq-au/core/roomeq/dsp/`
  (biquad, eq_chain) est partagé par le VST et l'AU (D23) et sa sérialisation
  JSON (`roomeq::preset::ToJson`) est le même format que les presets de
  l'application → l'APO relit la courbe avec ce code déjà validé
  (430 vérifications natives, parité bit à bit RoomEq.Core).
- **Ce qui ne change pas** : le pont ASIO (les DAW qui veulent du low-latency
  sortent sur notre ASIO intégré — user-mode, sans signature), la boucle
  WASAPI (repli sans installation), la mesure/calibration (l'APO passe en
  pass-through pendant les balayages pour mesurer la pièce, pas la
  correction).
- **macOS** : inchangé (le greffon HAL + périphérique virtuel y sont légitimes
  car pas de mur de signature équivalent).

### 3.4 Limites honnêtes (à dire aussi aux utilisateurs)

1. **Les flux WASAPI exclusif / ASIO / Kernel Streaming contournent le moteur
   audio**, donc l'APO (comme ils contournent déjà tout effet système). La
   quasi-totalité des applications tournent en mode partagé ; les DAW ont
   notre ASIO. Il faut le documenter, pas le cacher.
2. **Effet par périphérique** : on active l'EQ sortie par sortie (enceintes,
   casque, HDMI…) — l'UI le gère, c'est même un avantage (EQ différent par
   sortie = déjà le modèle de presets par périphérique de RoomEq).
3. **Remplacement de l'EFX du fabricant** s'il y en a un (Realtek, Dolby…) :
   nos valeurs s'installent **après sauvegarde** des originales, restaurables
   à tout moment ; l'UI mentionne que les « améliorations » du fabricant sont
   suspendues sur cette sortie pendant l'activation.
4. **Une mise à jour de pilote audio peut écraser les FxProperties** →
   l'application re-vérifie et répare automatiquement au démarrage et à chaque
   événement « périphérique audio modifié » (watcher existant, I-AUD-03).
5. **ARM64** : livrer une DLL ARM64 en plus du x64 (second lot, la chaîne de
   cross-compilation existe déjà pour le VST 32 bits).
6. **Contenu protégé (DRM)** : les flux protégés peuvent être traités en mode
   « raw » ; l'EFX reste appliqué aux flux raw (Microsoft Learn), mais le
   comportement précis varie selon les versions de Windows → à vérifier au
   test terrain (Netflix/Spotify en usage réel). Worst case honnête : ce flux
   précis n'est pas égalisé.
7. **Stabilité** : une APO défaillante peut interrompre le son système
   (audiodg). Notre cœur DSP est déjà éprouvé (VST/AU en production chez
   l'utilisateur) ; la spec impose en plus : aucune allocation ni I/O dans le
   chemin de traitement, tests de charge, pass-through en cas de config
   illisible.

### 3.5 Pourquoi pas les alternatives ?

| | **A — APO** (recommandé) | B — Signer `roomeqvad.sys` | C — Driver OEM signé tiers | D — Mode test (statu quo) |
|---|---|---|---|---|
| Coût récurrent | **0 €** | ≈ 226–669 $/an (certificat EV) + vérification légale d'entité | Licence/autorisation (VB-CABLE : donationware, autorisation à demander ; LoopBeAudio, e2eSoft VSC SDK : payants) | 0 € |
| Mode test / Secure Boot | **Non, jamais** | Non | Non | **Oui** (filigrane + faille sécurité) |
| Installation utilisateur | 1 UAC, immédiate, réversible | Idiome « driver » classique + redémarrage possible | Installation tierce + la nôtre | Redémarrage + filigrane + refus prudent par défaut (I-INS-05) |
| Couverture audio | Tout le mode partagé, par sortie | Tout (câble universel + ASIO) | Tout le câble tiers | Tout le câble |
| Dépendance | **Aucune** | Aucune | **Éditeur tiers** (contrat, pérennité) | Aucune |
| Travail | 1 vague (4 lots, § 4) | Paperasse + re-signature du pilote scellé (jamais recompilé depuis v2.2.2) | Intégration + négociation | Rien |
| Risque principal | Mécanisme standalone non officiel (mitigé : auto-réparation, 10 ans de recul Equalizer APO) | Coût récurrent, dépendance à une CA | Pérennité de l'éditeur, réputation SmartScreen | Expérience dégradée, insécurité |

- **B** reste la voie « propre » long terme si RoomEq se commercialise :
  elle seule donne le vrai câble universel signé à notre nom. Rien n'empêche
  d'y venir plus tard — les deux approches cohabitent (APO = mode standard,
  pilote signé = option studio).
- **C** remplace une dépendance (VB-CABLE) par une autre, avec un contrat en
  plus : antinomique avec l'objectif « ne plus dépendre de VB-Cable ».
- **D** est refusé par vous et par la politique produit (`I-INS-05`).

## 4. Décision proposée (D24) et plan de mise en œuvre

> **D24 (proposée)** — L'égalisation système Windows (« tout le son de
> l'ordinateur traverse l'EQ ») passe par un **Audio Processing Object**
> RoomEq (`RoomEqApo.dll`, user-mode, position EFX) : aucune signature
> Microsoft, aucun mode test, aucune dépendance tierce. Le pilote noyau
> `roomeqvad` (mode test) reste une option avancée documentée ; la signature
> EV/attestation (option B) n'est engagée que si le produit le justifie plus
> tard. Le pont ASIO et la boucle WASAPI sont inchangés.

**Vague 9 proposée — 4 lots** (spec contractuelle : `I/audio/I-apo-systeme.md`,
I-AUD-09 / M-AUD-09 / N-APO-01) :

1. **Natif** `natif/roomeq-apo/` : DLL COM x64 (+ ARM64 en option), EFX,
   cœur DSP réutilisé du cœur partagé D23, config `%ProgramData%`, poll
   mtime + swap atomique, pass-through bypass ; harnais de tests
   `roomeq_apo_host` (pattern du mini-hôte VST : instanciation, blocs,
   parité de courbe, continuité au rechargement, compteur d'allocations
   RT-safe).
2. **Registrar C#** `src/RoomEq.Audio/SystemApo/` : énumération des endpoints
   render, sauvegarde/restauration FxProperties (idempotent, testé sur faux
   registre en CI Linux), élévation UAC unique, anti-boucle (jamais sur
   « RoomEq Input »), magasin de courbe active + bypass mesure.
3. **UI + intégration** : panneau « EQ système » dans l'onglet Mixage (état
   par sortie, activer/désactiver/réparer), écriture de la courbe à chaque
   changement de l'onglet Égaliseur, auto-réparation au démarrage et sur
   DevicesChanged.
4. **Distribution** : déploiement par le `RoomEq-win-Setup.exe` existant
   (Velopack — une DLL de plus, aucun INF), désinstallation propre qui
   **restaure d'abord** tous les endpoints modifiés, documentation utilisateur
   (limites § 3.4 écrites noir sur blanc) et checklist de validation terrain
   (Realtek/Intel/NVIDIA/BT/HDMI, mise à jour de pilote, mode exclusif).

## 5. Sources consultées (1er novembre 2026)

- Microsoft Learn — *Loading Test Signed Code* :
  https://learn.microsoft.com/en-us/windows-hardware/drivers/install/the-testsigning-boot-configuration-option
- Microsoft Learn — *Test Signing* :
  https://learn.microsoft.com/en-us/windows-hardware/drivers/install/test-signing
- Microsoft Learn — *Register for the Hardware Developer Program* :
  https://learn.microsoft.com/en-us/windows-hardware/drivers/dashboard/hardware-program-register
- Microsoft Learn — *Driver Code Signing Requirements* :
  https://learn.microsoft.com/en-us/windows-hardware/drivers/dashboard/code-signing-reqs
- Microsoft Learn — *Attestation signing* :
  https://learn.microsoft.com/en-us/windows-hardware/drivers/dashboard/code-signing-attestation
- Microsoft Learn — *Audio Processing Object Architecture* :
  https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/audio-processing-object-architecture
- Étienne Dechamps — *APO* (notes techniques : moteur audio, sAPO,
  FxProperties, LocalService) :
  https://github.com/dechamps/APO/blob/master/README.md
- Equalizer APO — fils SourceForge « APO registry entries » et
  « Silent install » :
  https://sourceforge.net/p/equalizerapo/discussion/general/thread/a636ab2d84/
  et https://sourceforge.net/p/equalizerapo/discussion/general/thread/a1ca2e691e
- Prix certificats (octobre 2026) : https://my-ssl.com/learn/code-signing-certificate-price
  (EV 349–669 $/an CA directes) et https://www.sslmentor.com/ssl/code-signing
  (Certum EV cloud ≈ 226–299 $/an).
- VB-AUDIO — VB-CABLE (donationware, XP→Win11 x86/x64/ARM64, aucune mention
  de redistribution) : https://vb-audio.com/Cable/
- nerds.de — LoopBeAudio (driver WDM noyau, évaluation 60 min, licence) :
  https://www.nerds.de/en/loopbeaudio.html
- e2eSoft — Virtual Sound Card SDK (redistribution sous licence) :
  https://www.e2esoft.com/sdk/vsc-sdk
- WindowsForum — *Multiple APO Effect Pack* (APOs tierces sur Windows 11,
  janv. 2026) :
  https://windowsforum.com/news/system-wide-audio-apos-evaluating-the-multiple-apo-effect-pack-on-windows-11.396483
