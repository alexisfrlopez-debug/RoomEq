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
  +5 tests registrar)
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
