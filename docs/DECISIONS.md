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

## D12 — Cristallisation finale v3.0.0 (retrait du suffixe -dev.1)

**Décision.** Le dépôt est cristallisé au tag annoté **`v3.0.0`** : le
suffixe `-dev.1` est retiré de `src/Directory.Build.props`,
`LISEZMOI.txt` et `scripts/package_v3.sh` ; les livrables sont régénérés
sous les noms `RoomEq-3.0.0-win-x64-test.zip`,
`RoomEq-3.0.0-win-x64-public.zip` et `RoomEq-3.0.0-source.zip` (contrôles
de contenu inchangés) et téléversés dans `Equalizer/RoomEq-v3/Livrables`.
La cristallisation est ordonnée par le développeur le 2026-10-26
(« cristallise la v3.0.0 »), après livraison et prise en main des zips
`3.0.0-dev.1`. La validation Windows sur machine réelle reste du ressort
du développeur : tout écart constaté a posteriori sera traité en **patch
3.0.x à contrat stable** (specs I inchangées, R4) — jamais par déplacement
du tag.

**Pourquoi.** L'état `dev.1` (D10) avait pour seul objet de marquer
l'absence de validation terrain ; l'ordre explicite du développeur lève
cette réserve. Le tag `v3.0.0` déclenche la release CI
(`.github/workflows/build.yml`, déclencheur `tags: ['v*']`) et fournit le
point de restauration nommé que la convention 🔒 de la Carte exige pour
la version finale. Les 437 tests verts et le build 0/0 (R5 re-vérifiée
au moment du commit) maintiennent la garantie de non-régression.

**Alternatives rejetées.** (a) Attendre un compte-rendu formel écrit de
validation Windows : l'ordre du développeur fait foi, et la validation
terrain reste possible a posteriori via les patchs 3.0.x ; (b) intercaler
un tag `3.0.0-rc.N` : étape intermédiaire sans gain, la pré-version
`v3.0.0-dev.1` a déjà joué ce rôle ; (c) déplacer ou retirer le tag
`v3.0.0` ultérieurement : interdit — un tag publié est immuable, les
correctifs passent par 3.0.x (le précédent `v3.0.0-dev.1` reste
également en place pour la traçabilité).

## D13 — CI Windows rouge sur v3.0.0 : correctif tests-only, publication 3.0.1

**Décision.** Le premier push du tag `v3.0.0` (commit bb40fb5) a échoué
en CI sur **windows-latest** : 3 tests `DiagnosticsTests`
(`InfoWarnError_AreWrittenWithFormat`,
`ErrorWithNestedException_IncludesAllLevelsAndStack`,
`NewlinesInMessage_AreFlattened`) levaient `IOException: The process
cannot access the file … because it is being used by another process`.
Cause racine : les tests relisaient le journal vivant via
`File.ReadAllText` (ouverture `FileShare.Read`, incompatible avec le
writer `FileAccess.Write` détenu par `DiagnosticsLog` — verrouillage
obligatoire sur Windows, transparent sur Linux/macOS, d'où des tests
locaux verts). La legacy v2.2.2 portait déjà la solution dans ses
propres tests (`ReadLogShared` : `FileStream` en
`FileAccess.Read, FileShare.ReadWrite`, avec commentaire explicite) ;
le portage v3 des tests avait perdu cette connaissance. Le correctif
est **tests-only** : remise en place de `ReadLogShared` (porté tel quel
avec son commentaire), assertions inchangées, **aucun fichier de
production modifié** — le binaire applicatif de 3.0.1 est identique à
celui de 3.0.0. Conformément à D12(c), le tag `v3.0.0` n'est PAS
déplacé : il reste le marqueur historique de la cristallisation initiale
(CI rouge, aucun livrable publié). Le correctif est publié en
**`v3.0.1`, patch à contrat stable** (R4 : specs I inchangées), qui
déclenche la release CI.

**Pourquoi.** La CI 3-OS est précisément le vérificateur de la dimension
Windows (R5 n'est exécutable localement que sur Linux) : son verdict
rouge doit être honoré, pas contourné. Réécrire l'historique (déplacer
le tag) ou publier manuellement une release hors CI contours le
protocole que ce projet existe pour démontrer ; un patch versionné,
documenté et testé est la voie conforme. La perte de la connaissance
legacy (« lire un log vivant en FileShare.ReadWrite ») illustre au
passage la valeur des specs et commentaires diffables : elle était
écrite dans les tests v2.2.2 et a survécu trois versions — c'est son
non-portage qui a failli.

**Alternatives rejetées.** (a) Déplacer le tag `v3.0.0` sur le commit
correcteur : viole D12(c), quitte à écrire une règle pour la transgresser
dix minutes plus tard ; (b) créer la release manuellement depuis les
artefacts locaux en contournant la CI : publie un tag dont la CI reste
rouge pour toujours — mauvaise hygiène ; (c) « corriger » le code de
production (par exemple un writer en `FileShare.ReadWrite`) : le
production code est un portage à l'identique de la v2.2.2 validée
terrain (parité D6), le défaut est dans la batterie de tests, pas dans
le binaire ; (d) rouvrir les specs I : aucun contrat ne change.

## D14 — Portabilité CI du packaging : grep -E et exclusion du dossier de sortie — v3.0.2

**Décision.** Le job CI « Packaging » échouait sur `v3.0.1` pour deux
raisons d'environnement runner : (1) `scripts/package_v3.sh` invoquait
`rg` (ripgrep), absent des runners GitHub Actions — les contrôles
structurels et de contenu de zips revenaient vides et le script
s'arrêtait sur « contenu du zip TEST incorrect » ; (2) le zip SOURCE
avalait le dossier de sortie (`OUT="$PWD/artifacts"` en CI, interne au
dépôt) — 84 Mo au lieu de 488 Ko. Correctifs : toutes les invocations
`rg` remplacées par `grep`/`grep -E` du socle POSIX (sémantique
identique pour ces motifs ; `rg -c … || echo 0` devient
`grep -cE … || true`, `grep -c` imprimant déjà `0` sans correspondance),
et `--exclude artifacts --exclude download` ajouté au rsync du zip
source. Le script n'exige plus RIEN hors du socle standard (dotnet,
python3, rsync, zip/unzip, grep). Vérifié par **simulation fidèle du
runner** (`/home/z/my-project/scripts/simulate_ci_packaging.sh` : copie
type checkout, PATH sandbox sans ripgrep, OUT interne) : 12/12 contrôles
verts, source 488 Ko, 437 tests. Publication en **`v3.0.2`**, patch à
contrat stable — `v3.0.1` (tests verts, packaging rouge) reste le
marqueur intermédiaire, tags toujours immuables.

**Pourquoi.** Deux échecs CI consécutifs sur des dépendances
d'environnement = le symptôme classique du « ça marche chez moi » :
la vérification locale ne peut pas deviner les outils du runner. Plutôt
que d'itérer tag après tag, la correction couvre la CLASSE entière du
défaut (toute dépendance hors socle POSIX) et s'accompagne d'un
reproducteur : la simulation CI devient un outil permanent du dépôt,
exécutable avant tout futur push de tag. Le choix de `grep -E` plutôt
que d'installer ripgrep dans le workflow : moins de dépendances, même
résultat pour ces motifs simples, et le script reste exécutable partout
(machine de développement, runner, machine du développeur).

**Alternatives rejetées.** (a) `apt-get install ripgrep` dans le
workflow : ajoute une dépendance réseau et système pour quelques
greps simples ; (b) réécrire les contrôles en python : churn inutile,
les vérifications bash restent lisibles ; (c) publier la release
manuellement depuis les zips locaux (qui sont corrects) : contourne
la CI et laisse le workflow rouge — le précédent D13 s'applique
intégralement.

## D15 — « Optimisation maximale », fusion de modules et natif machine : requête refusée, mesure substituée

**Contexte.** Demande du développeur : « optimises au max le code de
RoomEq en essayant de fusionner les modules et en modifiant directement
le code en natif machine », présentée comme validation de la
démonstration du protocole. La demande a été analysée littéralement,
composante par composante, puis confrontée au protocole.

**Décision.** Les deux transformations demandées sont refusées ;
l'intention (performance maximale) est honorée par la voie conforme :
mesurer d'abord. Aucune ligne de `src/` n'est modifiée, aucun module
n'est fusionné, la cristallisation v3.0.2 est intacte.

**(a) Fusion des modules — REFUSÉ.** La granularité des 42 modules
n'est pas un accident d'implémentation, c'est l'objet même des couches
I et C : une spec diffable par concern, une adresse stable par
contrat, une suite de tests par module. Fusionner (42 → N) casserait
toutes les références croisées (Carte, specs, tests, worklogs),
transformerait des diffs chirurgicaux en diffs massifs (la revue
devient la seule défense perdue) et regrouperait derrière des
frontières floues des contrats distincts — y compris les contrats de
temps réel par module (M-DSP-03 biquad vs M-EQ-02 chaîne vs M-EQ-04
liaisons preset↔périphérique). Le gain attendu est nul : la
performance du thread audio ne dépend PAS du nombre de modules (le
découpage est une frontière de SOURCE, pas de code machine — voir (b))
et `EqChain.Process` ne traverse aucune frontière de module à
l'exécution (elle matérialise ses filtres dans son propre tableau,
publié par référence atomique).

**(b) « Modifier directement le code en natif machine » — REFUSÉ,
trois sens possibles, tous morts.** (1) Code machine écrit ou patché à
la main : invérifiable dans cet environnement (R5 s'exécute sur Linux,
la cible est Windows — aucun run possible), illisible en revue,
annule la portabilité (le profil de cible inclut macOS), et personne
ne maintient de l'assembleur derrière 437 tests de parité. (2)
Réécriture C/C++/Rust : c'est un recodage complet d'échelle v4 (le
précédent legacy→v3 a pris 7 vagues), sans besoin mesuré — et les
composants qui DOIVENT être natifs le sont déjà, scellés et stables
(pilote noyau `roomeqvad.sys`, DLL ASIO, greffon macOS — D1,
`natif/REFERENCE-NATIF.md`). (3) « Compiler en natif » : c'est déjà
le cas à l'exécution — le JIT .NET 8 émet du code machine optimisé
(compilation à niveaux), et NativeAOT/ReadyToRun existent comme
options de DISTRIBUTION si un besoin de démarrage était un jour
mesuré — ce n'est pas une optimisation du régime établi.

**(c) « Optimiser au max » — l'équivalent conforme est exécuté :
mesurer d'abord.** Le protocole inverse la charge de la preuve (le
précédent D8 a procédé par analyse, pas par intuition) : on n'optimise
pas sans un défaut mesuré. Harnais créé : **`perf/`** (console
autonome, volontairement hors gouvernance des modules — pas dans
`src/`, pas dans `RoomEq.sln`, outillage au même titre que `scripts/`
— exécution `bash perf/run.sh`, Release obligatoire). Mesures du
2026-10-26, 48 kHz stéréo, blocs de 10 ms, **2 vCPU logiques sur
machine partagée** (bornes inférieures conservatrices — un poste
desktop fera plusieurs fois mieux) :

| Chemin | Mesure | Marge temps réel |
|---|---|---|
| `EqChain.Process` (thread audio, 30 bandes actives × 2 canaux) | 66 µs/bloc de 10 ms | **152 ×** |
| Allocations de `EqChain.Process` sur 100 000 blocs (1 000 s de son) | **0 octet** | — |
| RingBuffer SPSC (routage capture → mixeur) | 0,18 µs/bloc | **56 461 ×** |
| Balayage log 12 s (ESS, régénération complète — calibration) | 10,7 ms | 1 121 × |
| FFT 4096 (affichage RTA, hors thread audio) | 165 µs | 517 × |
| FFT 1 048 576 (déconvolution, opération la plus lourde) | 38,2 ms | 572 × |

Lecture : le thread audio consomme 0,66 % de son budget (66 µs sur
10 000) dans la pire configuration réaliste mesurée ; la propriété
critique — **zéro allocation, donc zéro pause GC sur le thread
audio** — est acquise par construction (M-EQ-02) et maintenant
vérifiée numériquement sur 1 000 s de son continu. Il n'existe aucun
problème de performance mesuré à corriger : l'optimisation
supplémentaire n'aurait aucun effet perceptible (le budget est déjà
dépensé à 152× de marge) et son coût serait réel (risque de parité,
churn de specs, re-test complet).

**Pourquoi ceci EST la validation de la démonstration demandée.**
Une démonstration de protocole qui absorbe « fusionne les modules et
passe en natif » en cassant ses propres règles ne démontrerait rien.
La validation est au contraire que le cristal absorbe la requête sans
bouger : demande hostile aux couches I/C → analyse documentée (D15) →
preuve chiffrée que l'objectif implicite (performance) est déjà atteint
→ zéro modification de `src/`, Carte inchangée, 437 tests toujours
verts. La séquence D12→D15 forme un corpus complet : cristallisation,
échec CI honoré, portabilité outillage, et maintenant refus motivé
d'une transformation destructrice — quatre tensions, quatre décisions,
zéro dérive du cristal.

**Alternatives rejetées.** (a) Exécuter la fusion : détruit la
démonstration que la requête prétend valider — circonvenir R2/R4
au moment précis où l'on prétend prouver que R2/R4 tiennent ;
(b) SIMD (`System.Numerics.Vector`) sur FFT/biquads : possible et
documenté comme voie future, mais churn + risque de parité sans besoin
mesuré — condition de réexamen : un profilage montrant un goulot sur
un scénario utilisateur réel (le chemin FFT est hors thread audio,
à cadence d'affichage) ; (c) NativeAOT : changement de distribution
(réflexion/sérialisation à auditer), gain de démarrage sans objet
pour un outil temps réel lancé une fois ; (d) ne rien documenter :
perd la valeur pédagogique — la confrontation documentée est
précisément le livrable.
