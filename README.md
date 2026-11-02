# RoomEq v3 — recodage sous protocole TRIADE

Égaliseur acoustique temps réel (correction de pièce + corrections casque AutoEq).
Ce dépôt contient le **recodage complet de l'application** (projets C#) sous la
gouvernance du protocole TRIADE : chaque module est défini par une **intention
diffable** (couche I, dossier `I/`), implémenté par du **code versionné**
(couche B, `src/` + `tests/`) et cartographié à **adresse stable** (couche C,
`CARTE.md`).

- **Base fonctionnelle** : v2.2.2 (`alexisfrlopez-debug/RoomEq` @ deff25c)
- **État** : recodage **terminé, cristallisé et publié** (v3.0.2 ; v3.0.0/v3.0.1
  = étapes intermédiaires à CI rouge, non déplacées) — **v3.1.0** ajoute
  l'**export de preset vers FabFilter Pro-Q 3 (.ffp)** (D17, module
  M-IO-06) ; **v3.2.0** ajoute l'**anti-boucle au moment du routage** (D18 :
  un périphérique routé en entrée ne peut plus être choisi en sortie master —
  refus immédiat + sorties grisées dans l'onglet Mixage) et le **greffon
  AudioUnit « RoomEq Correction » `.component` pour Ableton Live** (D19,
  module natif M-AUD-07 : applique les presets RoomEq dans le DAW, parité
  bit à bit, CI macOS + auval) —
  44 modules 🔒, 461 tests verts (CI 3 OS) + 118 vérifications natives,
  parité v2.2.2 établie point par
  point (`docs/PARITE-v2.2.2.md`) ; les écarts terrain éventuels seront
  traités en patchs à contrat stable ; **v3.4.0** : **greffon VST 2 32 bits
  Windows « RoomEq Correction »** (D21, module natif M-AUD-08 — presets
  `.json`/`.ffp` dans un DAW 32 bits, cross-compilé Zig, parité bit à bit) ;
  **v3.4.1** : correctif du greffon VST suite au premier retour utilisateur
  (« aucune différence de son » — greffon transparent sans preset chargé) :
  **auto-chargement du preset le plus récent du magasin de l'application à
  l'ouverture**, bannière « Aucun preset chargé » dans l'éditeur, journal du
  premier bloc audio (spec I-AUD-08 v2, 264 + 38 vérifications) ; **v3.2.1** : correctif CI du greffon
  (inclusions libc++ macOS) + bundle universel arm64/x86_64 ; **v3.3.0** :
  **installation en 1 clic** (D20 — `RoomEq-win-Setup.exe` Windows via
  Velopack cross-compilé, `.pkg` macOS pour le greffon ; aucune dépendance
  ajoutée à l'application) ; **v3.7.0** : **EQ système sans pilote** (D24,
  module M-AUD-09 — le DSP RoomEq s'installe dans le moteur audio Windows
  via un Audio Processing Object : tout le son d'une sortie traverse
  l'égaliseur, sans signature Microsoft ni mode test ni VB-CABLE, latence
  ajoutée nulle, activation par sortie en une invite administrateur ;
  correctifs : fin des gels d'interface au rafraîchissement des
  périphériques — capture en une passe en arrière-plan — et latence du
  routage console réduite 50 → 30 ms contre la désynchronisation
  son/image en vidéo) ; **v3.8.0** : console allégée (D25 — tranches
  « entrée/sortie » retirées, EQ système en tête de l'onglet Mixage) ;
  **v3.9.0** : **console retirée, verdict APO visible, instance unique**
  (D26 — l'onglet Mixage devient le poste de pilotage de l'EQ système ;
  le verdict de chaque activation [succès comme échec] s'affiche en
  couleur dans le panneau et n'est jamais effacé par le rafraîchissement ;
  la sortie d'écoute suit la barre du haut ; anti double-correction
  étendue au lecteur interne ; l'application refuse de s'ouvrir plusieurs
  fois après l'installation) ; **v3.9.1** : correctif activation — les
  sorties sans emplacement d'effets déclaré (pilotes génériques Windows,
  ex. Realtek(R) Audio) acceptent enfin l'EQ système : la sous-clé
  FxProperties manquante est créée à l'activation (mécanisme Equalizer
  APO) et supprimée à la désactivation si redevenue vide (spec I-AUD-09
  v1.2, +4 tests registrar) ; **v3.9.2** : correctif activation (suite) —
  les clés des sorties MMDevices appartiennent à SYSTEM et refusent
  l'écriture aux administrateurs même élevés (« Requested registry access
  is not allowed ») : prise de possession + autorisation à l'activation
  (mécanisme takeOwnership/makeWritable d'Equalizer APO), sécurité
  d'origine (owner + DACL) sauvegardée avant écriture et restituée à la
  désactivation, message d'échec en français clair (spec I-AUD-09 v1.3,
  +5 tests registrar) ; **v3.9.3** : correctif critique — la prise de
  possession v3.9.2 n'a JAMAIS pu s'exécuter : la structure interop
  TOKEN_PRIVILEGES déclarait le LUID comme long C# (alignement 8), qui
  insérait 4 octets de padding — Windows recevait un LUID falsifié et
  ERROR_NOT_ALL_ASSIGNED, croyait « non élevé » et abandonnait (« toujours
  le problème de clé protégée »). LUID natif en deux DWORD (16 octets,
  verrouillé par 4 tests de marshaling sous Linux) + diagnostic par
  maillon remontant dans le verdict via ApoRegistryRefusalException —
  plus d'échec muet (spec I-AUD-09 v1.4, 488 → 493 tests) ; **v3.9.4** :
  correctif de binding P/Invoke — le diagnostic v3.9.3 a nommé le coupable
  (« le point d'entrée CloseHandle est introuvable dans la DLL
  advapi32.dll ») : le privilège de prise de possession était BIEN activé
  (le LUID v3.9.3 était correct) mais le nettoyage du jeton, importé de la
  mauvaise DLL (advapi32 au lieu de kernel32), levait
  EntryPointNotFoundException dans le finally et MASQUAIT le succès —
  l'activation abandonnait avant d'écrire. CloseHandle importé de
  kernel32.dll, DLL d'origine de chaque import natif verrouillée par 2
  tests réflexifs exécutables sous Linux (spec I-AUD-09 v1.5,
  493 → 495 tests) ; **v3.9.5** : volume re-ciblé après le redémarrage du
  service audio d'une activation EQ système (les objets COM WASAPI morts
  gelaient les deux curseurs : « la barre ne bouge plus / n'agit plus ») +
  chaîne de courbe observable dans le panneau (courbe envoyée, écriture,
  publication par le moteur audio lue dans son journal — un EQ inaudible
  nomme son maillon) + verrou ACL de %ProgramData% (icacls muet = EQ muet)
  (spec I-AUD-09 v1.6, 495 → 511 tests) ; **v3.9.6** : la DLL APO n'était
  JAMAIS chargée par le moteur audio (« journal APO absent » alors que
  l'inscription COM et le CLSID EFX étaient corrects) — il manquait les
  prérequis de la recette Equalizer APO : DisableProtectedAudioDG=1 (sinon
  audiodg en mode protégé refuse SILENCIEUSEMENT toute DLL non signée ;
  réglage réversible, valeur d'origine sauvegardée/restituée, un Equalizer
  APO installé n'est jamais touché), inscription auprès du moteur audio
  (clé AudioEngine\AudioProcessingObjects\{CLSID}, valeurs nommées —
  équivalent RegisterAPO du DDK) et mode de traitement EFX par défaut
  (MULTI_SZ). Le panneau nomme désormais le prérequis manquant et
  « Réparer » rétablit tout (spec I-AUD-09 v1.7, 511 → 531 tests) ;
  **v3.9.7** : « dès que l'EQ est activé sur une sortie, je n'ai plus de
  son sur cette sortie » — un APO non instanciée par le moteur audio TUE
  le graphe de l'endpoint (retour terrain documenté : « Failed to play
  test tone », mixeur vide). LE SON PASSE AVANT L'ÉGALISEUR : ACL du
  dossier bin « Tout le monde : lecture + exécution » (comme Program
  Files, vérifiée en relisant la DACL — l'octroi Audiosrv était
  meilleur-effort et son échec invisible), sonde WASAPI silencieuse
  post-activation avec ROLLBACK AUTOMATIQUE si la sortie ne répond plus
  (origine restaurée + service redémarré + verdict explicite), suivi de
  la sortie PAR DÉFAUT de Windows par l'écoute (volume + preset —
  l'appli restait sur les haut-parleurs pendant que le casque était le
  défaut : « barre de volume sans effet »), interface déclarée au moteur
  alignée sur SysVAD/Equalizer APO (APOInterface0 =
  IAudioProcessingObject) et journal de l'APO avec repli dans le profil
  du service (spec I-AUD-09 v1.8, 531 → 550 tests) ; **v3.9.8** : le
  retour terrain v3.9.7 ENFIN transmis complet (« activation du casque
  JBL échouée — le son a été préservé ») a montré un rollback efficace
  mais un verdict AVEUGLE (journal APO vide : la DLL jamais instanciée,
  aucune cause désignable). La source d'Equalizer APO lue intégralement
  contient le fait décisif : « bluetooth devices may be combined in
  Windows 11, EFX will not work then » — notre position EFX (,7) est
  exactement celle que la référence évite sur les périphériques combinés
  (Bluetooth Windows 11, dont le casque du retour terrain). TROIS
  correctifs : position ADAPTÉE AU PÉRIPHÉRIQUE (MFX ,6 pour les
  combinés, EFX sinon, slot mémorisé dans la sauvegarde, origine MFX
  capturée, notre CLSID périmé traité comme absent, réparation au slot —
  comportement Equalizer APO), AUTO-TEST COM PRÉ-VOL de la DLL dans le
  processus élevé AVANT toute écriture registre (chargement,
  CoCreateInstance via l'inscription réelle, 4 interfaces, enregistrement,
  LockForProcess, APOProcess sur un signal témoin, UnlockForProcess — un
  échec BLOQUE l'activation sans risque pour le son et nomme l'étape +
  son HRESULT), et verdict DIFFÉRENTIEL complet en cas d'échec moteur
  (auto-test, prérequis, trace de chargement, erreur exacte de la sonde
  WASAPI — l'exception n'est plus avalée —, inventaire des positions avec
  alerte multi*, version Windows) ; redémarrage du service audio
  attendant vraiment RUNNING (sc query, code numérique indépendant de la
  langue) (spec I-AUD-09 v1.9, 550 → 598 tests) ; **v3.9.9** : le retour
  terrain v3.9.8 ENFIN complet (capture du panneau : auto-test OK,
  prérequis tous verts, journal présent — et sonde WASAPI 0x80040110
  CLASS_E_NOAGGREGATION) a désigné NOTRE fabrique COM : elle refusait
  l'AGRÉGATION, ignorant que le moteur audio (audiodg) instancie TOUS les
  sAPO avec un contrôleur (« audiodg AGGREGATES APOs… Returning
  CLASS_E_NOAGGREGATION makes the host silently abandon the APO »,
  nnyj/virtual-surround-sound ; la fabrique d'Equalizer APO l'accepte :
  new EqualizerAPO(pUnkOuter) + NonDelegatingQueryInterface — exigence
  documentée nulle part, invisible sauf à écrire la fabrique à la main).
  Refus → moteur abandonne l'APO → graphe non construit → sortie MUETTE :
  c'est LE mécanisme qui coupait le son depuis le début. Correctifs :
  agrégation COM native (INonDelegatingUnknown à vtable binairement
  compatible IUnknown, IUnknown déléguant au contrôleur, identité interne
  non déléguante, fabrique miroir de la référence — 14 vérifications
  natives nouvelles), 10ᵉ étape d'auto-test qui rejoue la création
  AGRÉGÉE (contrôleur CCW + délégation d'identité vérifiée : ce défaut
  ne peut plus jamais atteindre le registre), et verdict non piégé par
  les lignes de l'auto-test dans le journal (marqueur de fin +
  classification ApoLogTrace : seule l'instanciation AGRÉGÉE prouve le
  moteur) (spec I-AUD-09 v1.10, 598 → 608 tests, APO 112 → 126)
- **Reprise** : lire `REPRISE.md` en premier
- **Décisions** : `docs/DECISIONS.md` (D1-D26) · **Carte** : `CARTE.md`
- **Natifs scellés** (pilote, ASIO, HAL macOS, installateurs) : `natif/REFERENCE-NATIF.md`
- **Greffon AudioUnit** (Ableton Live, macOS) : `natif/roomeq-au/README.md`
- **EQ système APO** (Windows, sans pilote) : `natif/roomeq-apo/README.md` +
  analyse `docs/EQ-SYSTEME-APO.md`

## Structure

```
I/          specs d'intention par module (une par module de la Carte)
src/        code source (RoomEq.Core + RoomEq.Audio + RoomEq.App)
tests/      suites xUnit (contrat exécutable de la couche I)
docs/       décisions, plan de recodage, audit de parité v2.2.2
natif/      référence des composants natifs scellés v2.2.2
perf/       harnais de mesure des marges temps réel (D15 — bash perf/run.sh)
scripts/    outillage (sync_backup.sh : tests + commit + GitHub + Drive ;
            package_v3.sh : livrables zips test/public/source ;
            build_setup.sh : installateur Windows 1 clic — D20)
```

## Vérification

```bash
export PATH="$HOME/.dotnet:$PATH"
cd src && dotnet build RoomEq.sln -c Debug     # 0 erreur 0 avertissement
dotnet test ../tests/RoomEq.Core.Tests/RoomEq.Core.Tests.csproj    # 237 verts
dotnet test ../tests/RoomEq.Audio.Tests/RoomEq.Audio.Tests.csproj  # 142 verts
dotnet test ../tests/RoomEq.App.Tests/RoomEq.App.Tests.csproj      # 79 verts
bash ../scripts/package_v3.sh                  # zips test/public/source
```

Fonction objectif : **Sécurité > Performance > Optimisation > Coût en tokens**.
