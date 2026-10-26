# Journal des décisions — RoomEq v3 (recodage Triade)

Chaque décision est irréversible sauf amendement explicite (nouvelle décision qui
la référence). Format hérité du protocole TRIADE. Fonction objectif :
**Sécurité > Performance > Optimisation > Coût en tokens**.

---

## D1 — Périmètre du recodage : l'application, pas les natifs

**Décision.** Le recodage complet vise l'application C# (`RoomEq.Core`,
`RoomEq.Audio`, `RoomEq.App`). Les composants natifs — pilote Windows
`roomeqvad.sys` (WDM/PortCls), DLL ASIO `RoomEqAsio64.dll`, greffon macOS
`RoomEqHAL.driver`, installateurs (MSI WiX, DriverSetup GUI WinForms, AppSetup) —
sont **conservés tels quels** dans leur version scellée v2.2.1 et référencés
comme blocs B scellés dans la Carte (`natif/REFERENCE-NATIF.md`).

**Pourquoi.** Les natifs sont validés par CI (9 rounds de fixes, 8/8 jobs verts),
signés en test, et fonctionnent chez l'utilisateur final (v2.2.1 a corrigé le bug
historique INF `$CHICAGO$`). Les réécrire n'apporte rien à la valeur produit et
exigerait un environnement de build Windows/WDK non disponible ici. La dette
accumulée se concentre dans le code C# (ViewModels de 1000+ lignes, duplication,
specs inexistantes).

**Alternative rejetée.** Tout réécrire y compris pilote et installateurs —
risque énorme sans gain, viole la fonction objectif (sécurité d'abord).

## D2 — Stack conservée, code intégralement réécrit

**Décision.** .NET 8 / C# 12, Avalonia 11, NAudio 2.2, xUnit, CommunityToolkit.Mvvm
sont conservés comme plateforme. Le code applicatif est **réécrit de zéro**
(clean-room informé par la legacy), pas copié-collé puis retouché.

**Pourquoi.** La compatibilité binaire avec les natifs v2.2.1 (P/Invoke, ponts
ASIO/CoreAudio, protocole de shared memory) impose la runtime .NET et les mêmes
points d'entrée. Réécrire le code tout en gardant la plateforme donne le gain de
nettoyage sans casser l'écosystème. `RoomEq.Core` reste **zéro dépendance
tierce** (règle déjà en place, test dédié conservé).

**Alternatives rejetées.** (a) Migrer vers Rust/C++ : incompatible avec le
calendrier et la CI existante ; (b) refactor incrémental du code v2 : la dette
structurelle (couplage vues/logique) rend le nettoyage incrémental plus coûteux
qu'une réécriture disciplinée.

## D3 — Ordre de recodage : bottom-up Core → Audio → App

**Décision.** Ordre : ① `RoomEq.Core` (DSP pur, zéro dépendance, entièrement
testable) ; ② `RoomEq.Audio` (E/S, routage, mesure) ; ③ `RoomEq.App` (GUI MVVM).
Chaque module est recodé avec : spec I → code → tests verts → marquage Carte.

**Pourquoi.** Core n'a aucune dépendance sortante et définit les types que les
deux autres couches consomment. Valider d'abord le cœur (FFT, filtres,
calibration) garantit que les couches hautes s'appuient sur du solide. C'est
l'application directe de R5 (verify-then-commit) à l'échelle des modules.

## D4 — Gouvernance Triade adaptée au code source

**Décision.** Dans ce dépôt : **couche I** = `I/<domaine>/I-*.md` (specs
diffables, une par module, ID stables) ; **couche B** = `src/` + `tests/`
(versionnés par git, scellés par tags `v3.x.y`, empreinte SHA-256 du tree dans
la Carte) ; **couche C** = `CARTE.md` à la racine (index de navigation, adresses
stables `M-<DOM>-<NN>`, statut de chaque module).

**Pourquoi.** Le protocole TRIADE v0.3 définit I/B/C pour du machine code ; pour
un dépôt de code source, l'équivalent structurel est : l'intention = la spec,
le binaire = le code compilé depuis une révision scellée, la carte = l'index des
modules. Les 7 règles s'appliquent telles quelles (jamais de B nu : tout fichier
de `src/` appartient à un module cartographié ; intention d'abord : la spec I
précède le code ; marquage : chaque commit porte son module ; patchs à contrat
stable : l'API publique d'un module ne change pas sans bump de version de spec ;
verify-then-commit : build + tests verts avant tout commit ; cristallisation :
tags ; adresses stables : IDs de module).

## D5 — Les tests sont le contrat exécutable de la couche I

**Décision.** Chaque module recodé embarque sa suite de tests xUnit (portée de
la legacy v2.2.1 et étendue). Un module n'est marqué « cristallisé » que si :
build 0 erreur 0 avertissement, 100 % de sa suite verte, spec I à jour.
Les tests contiennent les constantes numériques de référence (tolérances,
amplitudes, fréquences) extraites de la legacy.

**Pourquoi.** La suite v2.2.1 (239+ tests, 9 rounds de CI verte) encode le
comportement réellement validé ; c'est la formalisation la plus fiable de
l'intention. R5 l'exige : aucun code ne rejoint le dépôt sans vérification.

## D6 — Base de référence et version cible

**Décision.** Base de référence fonctionnelle : **v2.2.2** (GitHub
`alexisfrlopez-debug/RoomEq` @ `deff25c`, tag `v2.2.2` — corrige le bug interop
SP_DEVINFO_DATA 0x6F8 postérieur au dernier worklog Drive). Miroir de référence
local : clone git `roomeq-git-ref/` (code CI-vert) ; le miroir Drive
`roomeq-legacy/src-drive/` sert pour les documents hors dépôt. Le recodage
produit **RoomEq v3.0.0**. La parité fonctionnelle v2.2.2 est le critère de
non-régression de chaque couche.

**Pourquoi.** v2.2.2 est la dernière version livrée et testée (chaîne
installateur complète). v3.0.0 marque la rupture de structure (recodage)
sans rupture de fonctionnalités. Le clone git est préféré au miroir Drive
comme référence code : historique exact, absence de doublons, contenu
binairement vérifié par la CI.

## D7 — Sauvegarde systématique à chaque modification

**Décision.** Règle héritée du dépôt TRIADE (D13 là-bas) : après chaque lot de
travail — `scripts/sync_backup.sh` = build PDF éventuel (N/A ici) + commit git +
push GitHub (`alexisfrlopez-debug/RoomEq`, branche `v3-recodage` jusqu'à
stabilisation) + miroir Drive (dossier `Equalizer/RoomEq-v3`). Jetons réutilisés
depuis `triade/.gdrive/` (jamais commités).

**Pourquoi.** Trois resets d'environnement ont déjà coûté du travail au
développeur ; la redondance git + GitHub + Drive a prouvé son efficacité pour
RoomEq v2. Le travail de recodage ne doit jamais exister en un seul exemplaire.

## D8 — Seuil de détection du balayage relevé (8× → 1000×)

**Décision.** `SweepAnalyzer.DetectionSnr` = 1000 (contre 8 dans la v2.2.2) :
une capture est déclarée « balayage non détecté » quand le pic de la réponse
impulsionnelle ne dépasse pas 1000× la médiane.

**Pourquoi (mesures en sandbox, balayage 20 Hz→20 kHz, fs 48 kHz).** La
déconvolution divise par |X|² : une capture de bruit seul produit par effet
différenciateur un faux pic à retard ~0 avec un ratio pic/médiane ~186 (bruit
blanc) ou ~9,2 (bruit passe-bas réaliste) — au-dessus de l'ancien seuil 8 : la
v2.2.2 rendait alors des bandes absurdes au lieu du message d'aide (l'utilisateur
qui a mal routé sa capture voit une « correction » aléatoire). Un balayage réel,
même atténué, dépasse 1e5 (mesuré : 1,2e7). Le seuil 1000 rejette le bruit seul
avec quatre décades de marge et garde quatre décades de marge aux balayages
réels faibles. Comportement consigné dans la spec I-CAL-02 et testé
(`PureNoise_IsRejected` : bruit blanc ET passe-bas).

**Alternative rejetée.** Conserver 8 (parité stricte v2.2.2) : perpétuer un
défaut connu du comportement validé contredit l'objectif du recodage (nettoyage)
et la fonction objectif (sécurité d'abord : un utilisateur non averti appliquerait
une correction aléatoire à son système).

## D9 — Parité des données embarquées : bibliothèque casques portée à 321 (vague 7)

**Décision.** Les jeux de données embarqués de la v3 doivent être **identiques en
volume à la v2.2.2**. L'audit de parité a révélé que `headphone_presets.json`
était resté au jeu v2.0.0 (255 modèles) alors que la v2.1.0 l'avait porté à 321
(66 ajouts Bluetooth grand public, dont les séries JBL Tune/Live/Tour). Le JSON
v2.2.2 (321 modèles, 58 marques, empreinte `ffff00a5…` vérifiée) le remplace ;
la spec I-HPH-01 passe en v2 (contrat public inchangé — patch à contrat stable,
données étendues) ; le test `Parity_IncludesV21Additions` verrouille le plancher
à 320 modèles et la présence des ajouts v2.1.

**Pourquoi.** La parité fonctionnelle est le critère de non-régression (D6) : un
utilisateur v2.2.2 qui passe à la v3 ne doit perdre aucune reconnaissance de
périphérique — le preset automatique par sortie s'appuie précisément sur ces
modèles (« Casque (JBL Tune Flex 2 Stereo) » doit continuer d'être reconnu).

**Alternatives rejetées.** (a) Garder 255 : écart silencieux de 66 modèles ;
(b) fusion manuelle des 66 ajouts dans le JSON v3 : inutile, les schémas sont
identiques (vérifié champ à champ) et l'empreinte du fichier legacy garantit
l'exactitude.

## D10 — Cristallisation v3.0.0-dev.1, packaging et CI v3 (vague 7)

**Décision.** Le dépôt est cristallisé au tag **`v3.0.0-dev.1`** : les 42 modules
applicatifs passent 🔒 dans la Carte (contrats I gelés jusqu'à v3.0.0 finale).
Le packaging v3 est porté par `scripts/package_v3.sh` (adapté du
`package_2.0.0.sh` legacy) : publication win-x64 auto-contenue, déclinaison
`RoomEq.exe` (public) / `RoomEq-test.exe` (test), zips test / public / source
avec contrôles de contenu. La CI GitHub Actions v3 (`.github/workflows/build.yml`)
couvre les tests xUnit sur les 3 OS et le packaging applicatif — **sans** jobs
natifs : le pilote, la DLL ASIO et les installateurs restent consommés depuis
les artefacts scellés v2.2.2 (D1, `natif/REFERENCE-NATIF.md`), notamment
`RoomEqDriverSetup-2.2.2.exe` pour l'installation du pilote. Le suffixe
`-dev.1` (pré-version SemVer) marque l'état « recodage vérifié par tests,
validation utilisateur Windows réelle non encore effectuée ».

**Pourquoi.** Le plan de vague 7 exige un point de restauration nommé et des
marqueurs 🔒 ; 437 tests verts + build 0/0 justifient la cristallisation, mais
l'absence de validation sur machine Windows réelle interdit le tag v3.0.0 nu.
Réutiliser le schéma de packaging legacy (mêmes noms de fichiers, mêmes règles
public/test) garantit la continuité des habitudes de distribution du développeur.

**Alternatives rejetées.** (a) Tag `v3.0.0` direct : prématuré sans validation
terrain ; (b) pas de tag : le dépôt reste sans point de restauration nommé, la
convention 🔒 de la Carte ne peut pas s'appliquer ; (c) réécrire les scripts
d'installateurs : hors périmètre D1, risque sans gain.


## D11 — Reverse-specs de l'installateur scellé v2.2.2 (préparation vague 8)

**Décision.** Les contrats de l'installateur de pilote (RoomEqDriverSetup +
custom actions MSI, scellés v2.2.2, D1) sont désormais spécifiés sous forme
de specs I diffables : **`I/installateur/`**, 8 modules M-INS-01..08, statut
Carte « ⬜ spec prête » — code futur, éventuelle vague 8. Le binaire v2.2.2
reste LA référence exploitable ; ces specs figent le comportement validé et
verrouillent par des critères de test les trois défauts historiques qui ont
fait les versions : 0xE0000205 (INF legacy NT4 → validateur bloquant
M-INS-03), 0x6F8 (SP_DEVINFO_DATA 424 octets → invariant Marshal.SizeOf==32,
M-INS-02), libellés pnputil FR/mojibake (→ fragments « publi »/« orig » +
CP850 + repli scan, M-INS-04). M-INS-04 impose en outre le partage des
fixtures de parsing avec l'application (M-AUD-03 DriverSetupManager),
supprimant contractuellement la triple duplication code qui a laissé les
défauts vivre en trois copies.

**Pourquoi.** L'analyse des incidents d'installation (v2.0.x blocages
CFA/SmartScreen/Secure Boot, v2.2.1/v2.2.2 bugs interop et parsing) montre
que les défauts de CODE étaient tous de classe « contrat non écrit » :
structure Win32 déclarée de mémoire, format INF non validé, sortie d'outil
externe parsée sur libellés. La spécification anticipée est précisément le
remède TRIADE (R2, intention d'abord) : au moment du recodage, chaque
invariant sera déjà un test. Écrire les specs MAINTENANT fige aussi la
connaissance (elles dérivent du code v2.2.2 relu ligne à ligne, pas d'une
mémoire de session), ce qui protège le projet contre toute perte de contexte.

**Conditions de lancement de la vague 8** : validation Windows de la v3
effectuée ; décision prise sur la signature du pilote (EV/attestation,
SIGNATURE-ET-STORE.md phase A — elle détermine si les étapes certificat et
mode test survivent) ; environnement de build/test Windows disponible pour
R5. Le pilote noyau N-DRV-01 reste scellé dans TOUS les cas : stable, testé
terrain, jamais en cause dans les incidents, et invérifiable ici (pas de
WDK ni de machine de test — recoder sans pouvoir vérifier contredirait R5).

**Alternatives rejetées.** (a) Recoder l'installateur tout de suite : deux
chantiers non validés en parallèle, et la forme du code dépend de la décision
de signature ; (b) recoder aussi le pilote .sys : risque BSOD neuf sans
aucun gain — aucun incident ne lui est imputable ; (c) ne rien documenter :
la connaissance des contrats resterait dans le binaire et la mémoire des
sessions, exactement ce qui a permis aux trois bugs de survivre trois
versions.
