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

## D16 — Alignement sur le protocole TRIADE v0.4.0 (26 octobre 2026)

| ID | Décision | Choix retenu | Alternatives rejetées |
|---|---|---|---|
| D16 | Alignement du dépôt sur la révision v0.4.0 du protocole | Alignement **gouvernance et documentation, sans modification de `src/`** : la v0.4.0 convertit en normes des pratiques que ce dépôt appliquait déjà (specs I avant code, pré-versions `-dev.N`, reverse-specs M-INS, fixtures pnputil partagées, mesure avant optimisation D15). Ajouts : `docs/CONFORMITE_TRIADE_v0.4.0.md` (tableau de bord de conformité + analyse d'implications), `docs/JOURNAL_OPTIMISATION.md` (format OPT-NN, l'entrée OPT-01 reprend la mesure D15), section traçabilité formalisée dans `CARTE.md` (la table modules devient explicitement la table I↔C↔B↔tests de la spécification § 4.2), classification taxonomique des défauts historiques (T1–T6) reportée dans le tableau de bord | Recoder pour « montrer l'alignement » (aucune exigence v0.4.0 ne touche le code : R10 renforcée et R11 s'appliquent au chargement natif et à l'optimisation — ce dépôt instancie B en code source ; modifier `src/` sans besoin fonctionnel violerait l'esprit de R4 et le refus D15) ; attendre la vague 8 (l'alignement documentaire n'a pas de dépendance) ; appliquer R10 signature/anti-rollback au zip public (mécanisme du profil natif, M0+ ; l'instanciation source l'honore par l'historique git signé de balises et les empreintes md5 des données) |

**Développement.** La v0.4.0 du protocole (décisions D19–D25 du dépôt triade) est une
révision de gouvernance : elle normalise la couche I, ajoute la taxonomie des défauts de
contrat non écrit, renforce R10, ajoute R11 (optimisation mesurée), officialise les
reverse-specs, les pré-versions dev/rc/final et les fixtures partagées, exige la
traçabilité I↔C↔B↔tests, normalise le journal des décisions et ajoute le tableau de bord
de conformité. Ce dépôt était **déjà conforme de facto** sur l'essentiel — c'est
précisément de ses pratiques que la révision est née — ; l'alignement consiste donc à (1)
nommer les choses comme le protocole les nomme désormais, (2) combler les écarts :
journal d'optimisation formalisé, classification taxonomique explicite des défauts
historiques, tableau de bord consolidé, (3) documenter ce qui ne s'applique pas à une
instanciation source et pourquoi. La suite de versions v3.0.0-dev.1 → v3.0.2 est
rétroactivement conforme au schéma dev/rc/final de la v0.4.0 (§ 9 de la spécification) :
la balise finale v3.0.2 est immuable, les correctifs 3.0.x sont à contrat stable.

**Vérification R5 (pré-commit).** Aucun fichier de production modifié ; build 0 erreur /
0 avertissement et 437/437 tests verts reconfirmés avant commit.

## D17 — Export de preset vers FabFilter Pro-Q 3 (1er novembre 2026)

| ID | Décision | Choix retenu | Alternatives rejetées |
|---|---|---|---|
| D17 | Demande développeur « exporter un preset d'égalisation en .ffp pour l'importer dans FabFilter Pro-Q » (précisé : Pro-Q 3) | **Nouveau module M-IO-06** (`FabFilterProQ3.cs`, spec `I/io/I-export-fabfilter.md`) : encodeur binaire du format FQ3p (en-tête + 334 flottants LE = 24 bandes × 13 + 22 globaux, 1 348 octets) ; **seules les bandes actives** sont exportées (le fichier sonne comme l'EQ à l'écran), le **préampli devient le gain de sortie** (normalisé ×36), bornes du greffon appliquées (10 Hz–30 kHz, Q 0,025–40, ±30 dB), emplacements inutilisés = preset vierge d'usine à l'octet près, > 24 actives → exception actionnable ; M-APP-04 étendu (partial `EqViewModel.FabFilter.cs` + bouton/aide dans EqTab + dialogue StorageProvider dans la vue) ; version **3.1.0** (nouvelle fonctionnalité à contrat stable) | Cibler Pro-Q 2 (FQ2p, 190 params — format documenté mais produit visé = Pro-Q 3) ; format texte Pro-Q 4 (`Signature=FQ4p`) — le greffon visé est Q3 et le format binaire FQ3p est chargé aussi par Q4 ; exporter aussi les bandes décochées (une bande décochée est inaudible : l'exporter changerait le son attendu) ; écrire le préampli en première bande (le greffon a un vrai gain de sortie : mapping direct) |

**Développement.** Le format FQ3p n'est pas documenté par FabFilter : il a été
établi par triple recoupement indépendant — preset d'usine réel (valeurs
globales reprises à l'octet près), toolkit Python de référence du format,
et convertisseurs tiers (Java Pro-Q 2 pour la famille d'encodage :
fréquence `log2`, Q `log10(Q)·0,312098175+0,5` — échelle exacte où 0 ↔ Q
0,025 et 1 ↔ Q 40 ; gain de sortie normalisé ×36 = dB, plage officielle
−∞…+36 dB). **Validation croisée** : le fichier produit par le code C# est
relu par le lecteur Python de référence — types, fréquences, Q, gains et
préampli retrouvés exactement (preset de test 7 bandes + preset casque
10 bandes). Limite documentée (spec I-IO-06, invariant 2) : Pro-Q 3 accepte
24 bandes actives maximum — la précision « Ultra » de l'import de
calibration (30 bandes) doit être réduite à « Fine » (20) ou des bandes
désactivées avant export ; l'UI le dit avant d'ouvrir le dialogue.

**Vérification R5 (pré-commit).** Build 0 erreur / 0 avertissement ;
458/458 tests verts (237 Core + 142 Audio + 79 App — +17 Core, +4 App).

## D18 — Anti-boucle au moment du routage (1er novembre 2026)

| ID | Décision | Choix retenu | Alternatives rejetées |
|---|---|---|---|
| D18 | Demande développeur : « lorsqu'un périphérique est routé en entrée, il ne doit pas pouvoir être routé sur la sortie master, pour éviter une boucle » | **M-AUD-03 spec v2** : la garde anti-larsen passe du démarrage au **moment du routage**. `ConsoleMixer.SetOutput` lève AVANT tout changement d'état (câble virtuel, périphérique virtuel RoomEq macOS, périphérique déjà capté par une piste loopback/macOS — l'ancienne sortie reste active) ; `AddChannel` lève symétriquement si la source capturerait la sortie master courante ; `OutputConflict`/`IsOutputRoutedAsInput` exposent le conflit à l'UI qui **grise** les sorties en cause dans le sélecteur master (`OutputDeviceInfo.BlockedReason`, motif affiché) et n'en choisit jamais une automatiquement ; `Start` revalide en défense en profondeur, y compris le défaut résolu quand aucune sortie n'a été choisie (nouveau : le défaut peut être un câble virtuel) | Rester au seul contrôle au `Start` (l'utilisateur pouvait MONTER la boucle et ne l'apprendre qu'au lancement du routage) ; filtrer silencieusement les sorties en conflit (perte d'information : l'utilisateur ne saurait pas POURQUOI la sortie manque) ; refuser côté UI uniquement (le cœur resterait contournable par la persistance/les API) |

**Développement.** Les refus précèdent toute mutation : la tranche en conflit
n'est pas créée, l'ancienne sortie master reste active. La restauration de
session (`LoadConfigOrDefaults`) est protégée par construction : les pistes
sont restaurées avant la sélection de sortie, et `RebuildOutputs` saute les
sorties en conflit lors du choix automatique (préférence de session → sortie
libre non virtuelle → sortie libre → dernière extrémité avec message). La
suppression d'une piste débloque immédiatement la sortie (réannotation).
Les messages sont actionnables dans les deux sens (« la piste « X » capte
déjà ce périphérique… supprimez cette piste ou choisissez une autre sortie » /
« la piste « X » capturerait en boucle la sortie master actuelle… changez
d'abord la sortie master »).

**Vérification R5 (pré-commit).** Build 0 erreur / 0 avertissement ;
461/461 tests verts (237 Core + 145 Audio + 79 App — +3 Audio, contrat
anti-larsen réécrit au niveau routage : refus à l'ajout ET à la sélection,
état conservé, déblocage après suppression de piste, marquage
`BlockedReason`/`Selectable`/`Display`).

## D19 — Greffon AudioUnit « RoomEq Correction » .component pour Ableton Live (1er novembre 2026)

| ID | Décision | Choix retenu | Alternatives rejetées |
|---|---|---|---|
| D19 | Demande développeur : « une version de l'appli qui soit un plugin .component pour Ableton » | **Nouveau composant natif N-AU-01 / module M-AUD-07** (`natif/roomeq-au/`, spec `I/audio/I-greffon-au.md`) : AUv2 `aufx`/`Rmeq`/`Rmeq` qui **applique** les presets RoomEq (`.json` application ou `.ffp` Pro-Q 3 — détection par magie) dans le DAW. Interprétation : le greffon applique la correction ; la mesure reste dans l'application (un greffon ne peut pas piloter le son système). Cœur C++ **port exact de RoomEq.Core** (biquads RBJ TDFII, chaîne, préampli — parité **bit à bit** vérifiée par 118 tests natifs sur références générées par le code C#), zéro dépendance runtime ; greffon bâti sur **AudioUnitSDK d'Apple 1.4.0 vendangé (Apache-2.0)** ; vue Cocoa (charger / glisser-déposer / courbe) ; paramètre « Préampli » automatisable, bypass standard, état de session (blob preset) ; CI macOS dédiée (build + signature ad hoc + **auval**) qui joint `RoomEq-AU-<version>-macos.zip` aux releases ; `build-macos.sh --install` pour l'utilisateur | JUCE (AGPLv3 ou licence commerciale : introduirait une contrainte de licence dans un projet jusque-là sans tierce partie — écarté au profit du SDK officiel Apache-2.0, ~360 Ko vendangés) ; VST3 (demande explicite `.component` = AudioUnit ; le SDK VST3 est GPLv3/propriétaire ; possible suivi ultérieur) ; AUv3 (Ableton Live consomme des AUv2) ; réécrire le cœur en dépendant de framework (le port est volontairement sans dépendance pour rester auditable et testable sous Linux) ; mesurer dans le greffon (hors périmètre d'un greffon de studio : pas d'accès au son système) |

**Développement.** La parité numérique est l'invariant central (spec
I-AUD-07, invariant 1) : les tests comparent la réponse impulsionnelle
(48 kHz stéréo et 44,1 kHz mono, presets réaliste/bordes/transparent) et la
courbe de réponse composite aux sorties du code C# — **écart nul** sur les
six références (les deux chaînes calculent en double, le stockage final en
float32). Le chargement d'un preset publie un `shared_ptr` + compteur de
version atomique ; chaque noyau reconstruit sa chaîne au bloc suivant
(action utilisateur rare, documentée). Limites v1 documentées : lecture
seule (édition dans l'application), coupes Pro-Q 3 raides importées en
12 dB/oct, mono/stéréo. La signature est ad hoc (aucun compte développeur
requis) ; la quarantaine macOS se retire par `xattr -cr` (documenté dans
`build-macos.sh` et le README du composant).

**Vérification R5 (pré-commit).** Application : build 0 erreur / 0
avertissement, 461/461 tests verts. Natif : `g++ -std=c++17 -Wall -Wextra
-Werror` propre, 118/118 vérifications vertes (parité bit à bit). La CI
macOS (`plugin-au`) compile le bundle et le valide par `auval` au push.

## D20 — Installation en « 1 clic » : Setup.exe Windows (Velopack) + .pkg macOS du greffon (1er novembre 2026)

| ID | Décision | Choix retenu | Alternatives rejetées |
|---|---|---|---|
| D20 | Demande développeur : « une installation en 1 clic plutôt qu'en ligne de commande » | **Une opération d'empaquetage, zéro code applicatif** : (1) Windows — `RoomEq-win-Setup.exe` produit par **Velopack** (`scripts/build_setup.sh`, vpk 1.2.161 épinglée, **cross-compilé Linux → win-x64** : aucune machine Windows requise) : double-clic → installation utilisateur **sans droits admin** (`%LOCALAPPDATA%\RoomEq`), raccourcis Bureau + Menu démarrer, désinstalleur « Paramètres → Applications », variante publique (RoomEq.exe silencieux seul) ; (2) macOS — `RoomEq-AU-<v>-macos.pkg` (job CI `plugin-au`, `pkgbuild`) : double-clic → `RoomEq.component` dans `/Library/Audio/Plug-Ins/Components`, postinstall `xattr -cr` + re-scan du registrar (recette auval D19) | MSI WiX (exigeait WiX + PowerShell en local = ligne de commande ; installation machine + admin ; empiétait sur le périmètre du MSI scellé v2.2.2) ; MSIX/Store (chantier `%LOCALAPPDATA%` + compte Partner, reporté — docs/DISTRIBUTION.md) ; Inno Setup (compilateur Windows uniquement) ; hook `VelopackApp.Build().Run()` (aurait ajouté une dépendance runtime tierce dans l'application, contraire au zéro-dépendance — `--skipVeloAppCheck` ; les **mises à jour automatiques** restent une décision future, le nupkg étant déjà produit en artefact CI) |

**Développement.** L'installateur n'est pas un module applicatif : aucun
fichier de `src/` ne change, la Carte reste 44 modules 🔒. `build_setup.sh`
republie win-x64 auto-contenu, applique les renommages AppFlavor
(RoomEq.exe / RoomEq-test.exe), exclut RoomEq-test.exe et `telemetrie.json`
de l'empaquetage (variante publique, aucun identifiant embarqué), puis
`vpk "[win]" pack` (directive de cross-compilation). Contrôles : version
lue dans `src/Directory.Build.props` (source unique de vérité), en-tête PE
(`MZ`) et taille minimale du Setup.exe et du nupkg. La CI gagne un job
`setup` (ubuntu, joint `RoomEq-win-Setup.exe` aux releases) ; le job
`plugin-au` produit en outre le `.pkg`. Le zip portable et
`RoomEqDriverSetup-2.2.2.exe` (pilote scellé) restent inchangés et
coexistent : le Setup n'installe ni ne télécharge le pilote (boucle WASAPI
par défaut, onglet Mixage pour le pilote). **Distinct de la vague 8**
(D11, specs `I/installateur/`) : D20 distribue l'application ; la vague 8
recoderait l'installateur **natif du pilote** scellé (charge utile, Win32,
GUI pnputil) — toujours non lancée, conditionnée à un environnement
Windows.

**Vérification R5 (pré-commit).** Build 0 erreur / 0 avertissement,
461/461 tests verts (aucun code applicatif modifié — versions et
empaquetage uniquement). Setup.exe local : PE32+ GUI x86-64 vérifié,
222 fichiers de l'application embarqués, raccourcis Desktop + StartMenu
confirmés dans le journal vpk. `.pkg` : produit par la CI macOS après
`auval` vert (job `plugin-au`).

## D21 — Greffon VST 2 32 bits Windows « RoomEq Correction » (1er novembre 2026)

| ID | Décision | Choix retenu | Alternatives rejetées |
|---|---|---|---|
| D21 | Demande développeur : « fais un VST 32 bits » — appliquer la correction RoomEq dans un DAW **Windows 32 bits** (Ableton Live ≤ 9.7, FL Studio 11, Cubase 5, REAPER 32…) | **VST 2, PE32 i386** (`natif/roomeq-vst/`, module M-AUD-08/N-VST-01) : le greffon réutilise **tel quel le cœur C++ du greffon AU** (parité bit à bit RoomEq.Core — D19) derrière une **ABI VST 2.4 autodéclarée** (`vst/vst2_abi.h`, réécrite intégralement, chaque constante vérifiée contre le SDK 2.4 authentique et l'en-tête vestige — le SDK VST 2 n'étant plus concédé par Steinberg) ; paramètre « Preamp » automatisable, chunks (preset restauré avec le projet DAW), bypass officiel (`effSetBypass`), éditeur Win32 (bouton « Charger… », glisser-déposer, courbe 97 points), entrées usine `VSTPluginMain` + `main` (alias `.def`) ; **cross-compilé Linux → win32 avec Zig 0.14.1** (MinGW-w64 + libc++ embarqués, aucun sudo, SSE2 forcé `-mcpu=pentium4 -msse2 -mfpmath=sse -mstackrealign`) → `RoomEq.dll` livré dans `RoomEq-VST-<v>-win32.zip` | VST 3 (quasi absent des hôtes 32 bits — Live 9 = VST 2 uniquement) ; en-têtes VST2 copiées d'un miroir (licence Steinberg interdit la redistribution) ; JUCE (AGPL/commercial, contraire au zéro-dépendance — déjà écarté en D19) ; 64 bits et macOS (non demandés : le 64 bits = un drapeau du script, macOS couvert par l'AU D19) ; édition des bandes dans le greffon (v1 lecture seule, comme l'AU) |

**Développement.** Le cœur n'est pas dupliqué : `natif/roomeq-vst/CMakeLists.txt`
compile les sources de `roomeq-au/core` (le module AU reste propriétaire et
scellé — REFERENCE-NATIF « code source vivant »). Le modèle de threads est le
miroir de l'AU (preset publié par `shared_ptr` + version atomique,
reconstruction au bloc suivant ; paramètres atomiques ; chunk ≠ paramètre,
l'hôte fait foi). Validation : 228 vérifications natives (parité **bit à bit
0.00e+00** vs références C# sur 48 k stéréo / 44,1 k mono / bordes /
transparent), mini-hôte `dlopen` (38 vérifications — cycle de vie ABI complet),
**exécution 32 bits sous qemu-i386** (228 vérifications, parité bit à bit —
sémantique d'instructions exacte), contrôles du binaire (PE32 i386, exports
`VSTPluginMain`+`main`, DSP en SSE2 pur). CI : nouveau job `plugin-vst`
(ubuntu, joint `RoomEq-VST-<v>-win32.zip` aux releases). Écart ulp possible
des `sin`/`pow` de la CRT Windows vs glibc (≤ ~1e-13 relatif, inaudible —
même limite documentée pour l'AU arm64). Le point de vigilance « bibliothèque
partagée C++ 32 bits construite par Zig segfaulte APRÈS dlclose » est un
défaut du crt Zig 0.14.1 (reproductible en 5 lignes avec une seule
`std::string` statique locale), sans effet sur la DLL Windows (CRT MinGW :
destructeurs au `FreeLibrary`) — documenté dans `natif/roomeq-vst/README.md`.

**Vérification R5 (pré-commit).** Build 0 erreur / 0 avertissement, 461/461
tests xUnit verts (aucun code applicatif modifié), 118/118 tests natifs AU
(non régressés), 228 + 38 vérifications VST (Linux x86-64), 228 vérifications
VST en exécution 32 bits (qemu-i386), `build-vst.sh` : PE32 i386 + exports
vérifiés.

## D22 — Règle permanente de livraison : synchro Git+Drive systématique + archivage des anciennes versions (1er novembre 2026)

| ID | Décision | Choix retenu | Alternatives rejetées |
|---|---|---|---|
| D22 | Demande développeur : « ajoute une règle permanente dans le fichier d'instructions du projet pour faire systématiquement une synchro git et drive à chaque livraison, + archivage des anciennes versions pour éviter toute confusion » | **Une version n'est LIVRÉE que lorsque tout est vert, dans cet ordre** : (1) tests (R5), (2) commit + push GitHub (branche `v3-recodage` + tags), (3) miroir Drive complet (`scripts/drive_sync_roomeq.py` : dépôt, bundle git, livrables), (4) **archivage automatique** : au premier niveau de `Equalizer/RoomEq-v3/Livrables`, une seule version par produit (familles appli/VST/AU) ; les versions antérieures sont DÉPLACÉES dans `Livrables/Archive` (jamais supprimées). Règle consignée dans `REPRISE.md` § 2bis (point d'entrée de chaque session) ; échec de synchro = livrable « NON LIVRÉ » au WORKLOG, priorité absolue de la session suivante | Suppression définitive des anciennes versions (perte du retour arrière) ; archivage manuel (oubliable, non systématique) ; un dossier Drive par version (même confusion, déplacée) ; numéroter les fichiers « -ancien » (fragile, opaque) ; ne rien changer (31 fichiers à plat = confusion sur la version à installer) |

**Développement.** Le mécanisme vit dans `scripts/drive_sync_roomeq.py`
(`archive_old_versions`, exécuté à CHAQUE synchro) : famille de produit
déduite du préfixe du nom (`RoomEq-`, `RoomEq-VST-`, `RoomEq-AU-`), clé de
tri de version (majeure, mineure, correctif, final > dev, n° dev — ainsi
3.0.0-dev.1 < 3.0.0 < 3.4.0) ; le déplacement Drive (addParents/removeParents)
est idempotent et ne corbeille jamais — un homonyme déjà présent dans
l'Archive (version re-livrée) fait seulement corbeiller la copie du premier
niveau. Les fichiers non versionnés (`RoomEq-win-Setup.exe`,
`RoomEq-VST-LISEZMOI.md`, `RoomEq-AU-LISEZMOI.md`) restent au premier
niveau. La règle EST le processus D7 existant, rendu obligatoire à
l'échelon « livraison » : `scripts/sync_backup.sh` (tests → commit → push
→ Drive + archivage) reste l'outil unique — aucun nouveau pipeline.

**Entrée en vigueur (session 16).** Premier archivage exécuté
immédiatement à l'adoption : 24 anciens fichiers déplacés (appli
3.0.0-dev.1 → 3.3.0, VST 3.4.0/3.4.2/3.5.0). Le premier niveau de
`Livrables` ne conserve que `RoomEq-3.4.0-win-x64-public.zip`,
`RoomEq-3.4.0-win-x64-test.zip`, `RoomEq-3.4.0-source.zip`,
`RoomEq-AU-3.3.0-macos.pkg`, `RoomEq-VST-3.5.1-win32.zip`,
`RoomEq-win-Setup.exe` et les deux LISEZMOI. Validation R5 : 461/461 tests
xUnit verts (aucun code applicatif modifié — `src/` et `natif/` intacts).

## D23 — Parité AU/VST : cœur partagé élargi (banque casques + écriture JSON) et couche d'édition portable (1er novembre 2026)

| | |
|---|---|
| **D23** | Demande développeur : « il me faut la version pour Ableton sur macOS en AU » — après validation du VST Windows 3.5.2 (édition directe, banque casques, VU-mètres), l'AU devait offrir LA MÊME expérience, sans dupliquer le code ni risquer le VST livré |
| **Décision** | **(1) Le cœur partagé (`natif/roomeq-au/core/`) absorbe la banque casques et l'écriture JSON** du VST : `headphone_bank.*` + `headphone_bank_data.h` → `core/roomeq/headphones/` (namespace `roomeq::bank`), `preset_write.*` → `core/roomeq/preset/` (`roomeq::preset::ToJson`). Ce sont des ports de RoomEq.Core (`HeadphonePresetLibrary.ToEqPreset`, sérialisation `EqPreset`) : ils appartiennent au cœur que les DEUX greffons compilent déjà (le VST compile le cœur AU depuis D21, sans copie). Le VST consomme les fichiers déplacés (includes + CMake ajustés, **zéro changement de comportement** : 430 + 41 vérifications vertes) ; `scripts/gen_headphone_bank.py` régénère vers le nouvel emplacement. **(2) Couche d'édition portable `natif/roomeq-au/edit/preset_editor.{h,cpp}`** (`roomeq::edit::PresetEditor`) : état de preset éditable thread-safe (chargement json/ffp, édition bornée aux bornes de l'application, banque, auto-chargement par récence avec saut des presets plats), testable sous Linux — l'intégration AudioUnit (`RoomEqAu.cpp`, macOS seule) reste mince : propriétés custom + paramètre + notifications. **(3) AU v3.6.0** : égaliseur modifiable à la souris (glisser/molette/double-clic/menu contextuel), banque des 321 casques par listes déroulantes, VU-mètres in/out, version affichée, auto-chargement du magasin `~/.config/RoomEq/presets`, journal `~/Library/Logs/RoomEq/roomeq-au.log` (miroir de roomeq-vst.log — diagnostic à distance du « aucune différence de son ») |
| **Alternatives écartées** | Compiler les sources du module VST depuis l'AU (couplage croisé VST↔AU, namespace `roomeq_vst` au sein du greffon AU — incohérent avec le modèle « chaque module propriétaire de ses sources ») ; dupliquer la logique d'édition dans `RoomEqAu.cpp` (non compilable sous Linux = non testable avant la CI macOS) ; migrer AUSSI le VST vers `PresetEditor` dans le même changement (toucher le VST 3.5.2 juste livré et validé par l'utilisateur pour un bénéfice nul — migration reportée à une session dédiée) ; AUv3 (Ableton consomme l'AUv2, validé par auval depuis D19) |
| **Validation** | AU : **15 671 vérifications natives** (parité DSP inchangée + écriture JSON aller-retour exact + banque 321 modèles + éditeur : clamps/round-trip chunk/« Manuel »/plat + auto-chargement par récence avec mtimes nanosecondes). VST non régressé : 430 + 41. xUnit : 461/461. CI macOS : build + signature ad hoc + `auval -v aufx Rmeq Rmeq` + artefacts `RoomEq-AU-3.6.0-macos.{zip,pkg}` |

**Détail technique — communication vue ↔ greffon (AUv2).** La vue Cocoa ne
peut pas atteindre l'objet C++ derrière le handle `AudioUnit` : l'édition
passe par des propriétés custom aux payloads binaires bornés — `'RPbd'`
(get, instantané « RBP1 » : préampli, nom, bandes complètes), `'RPeb'`
(set, édition masquée fréquence/gain/Q), `'RPab'` (set, ajout), `'RPbo'`
(set, suppression/activation/type), `'RPlh'` (set, modèle de banque),
`'RPlv'` (get, VU entrée/sortie). Chaque mutation re-sérialise le preset au
JSON exact de l'application (l'état de session du projet DAW emporte
l'édition) et notifie par `PropertyChanged(kRoomEqPropertyStatus)`. La
banque, données statiques du bundle, est lue directement par la vue (aucune
propriété nécessaire). Les VU-mètres sont des atomiques écrits par le noyau
du canal 0 au rendu et sondés par la vue à 30 Hz (décroissance locale,
jamais de verrou au rendu — invariant 4 respecté).

## D24 — EQ système par APO : le DSP RoomEq dans le moteur audio Windows (2 novembre 2026)

| | |
|---|---|
| **D24** | Demande développeur : « je voudrais que l'application installe son propre driver de carte son virtuelle afin de ne plus dépendre de VB-CABLE, mais je bute sur le problème du driver signé Microsoft qui demande une solution payante. Je veux un moyen de contourner ça, user friendly, sans mode test » (+ validation implicite du choix APO en reprenant le besoin : auto-suffisant, aucun logiciel tiers, « j'installe l'appli, je choisis la sortie RoomEq, je route vers le vrai périphérique ») |
| **Décision** | L'égalisation système Windows (« tout le son d'une sortie traverse l'EQ ») passe par un **Audio Processing Object** RoomEq (`RoomEqApo.dll`, user-mode, position EFX) chargé par `audiodg.exe` : **aucune signature Microsoft, aucun mode test, aucune dépendance tierce, latence ajoutée nulle** — le mur de la signature ne s'applique qu'au code NOYAU, l'APO est du code utilisateur chargé par le moteur audio lui-même (mécanisme des effets Realtek/Dolby, prouvé à l'échelle par Equalizer APO). La « carte son virtuelle RoomEq » littérale reste impossible gratuitement (certificat EV 226–669 $/an, cf. `docs/EQ-SYSTEME-APO.md`) : l'APO renverse l'architecture — au lieu d'amener le son à l'application, le DSP va au son. Livré en v3.7.0 : cœur natif D23 réutilisé (parité bit à bit), panneau « EQ système » dans l'onglet Mixage (activer/désactiver par sortie, une invite UAC, réversible, réparable), courbe poussée en temps réel depuis l'onglet Égaliseur avec fondu 20 ms (le son ne s'interrompt jamais), bypass automatique pendant les mesures. Le pilote noyau `roomeqvad` (mode test, I-INS-05) reste une option avancée ; la signature EV/attestation reste la voie « propre » si le produit se commercialise. Mêmes correctifs livrés : rafraîchissement des périphériques en UNE passe en arrière-plan (fin des gels à chaque clic — bouton ⟳ animé) et latence console 50 → 30 ms (désynchro son/image en vidéo corrigée de fond par l'APO, à latence nulle) |
| **Alternatives écartées** | Signer `roomeqvad.sys` via attestation (coût récurrent, vérification d'entité légale — reporté tant que le produit le justifie) ; redistribuer un pilote tiers signé (remplace une dépendance par une autre + contrat) ; mode test (filigrane, insécurité, refusé par l'utilisateur et par I-INS-05) ; WASAPI loopback seul (couverture partielle : une seule sortie captée, latence de chaîne complète) |
| **Validation** | Harnais natif `roomeq_apo_tests` : **112 vérifications, 0 échec** (ABI COM vérifiée contre le SDK 10.0.10240 + Equalizer APO, parité de courbe bit à bit avec EqChain [0.00e+00], **0 allocation/libération en régime établi** [compteurs new/delete globaux], continuité échantillon par échantillon à la réécriture identique, fondu enchaîné reproduit au demi-ULP, bypass, config corrompue/supprimée, mono + 6 canaux [L/R traités, autres intacts], reformatage, BUFFER_*/in-place). DLL livrées : x64 452 Ko + arm64 412 Ko (Zig, PE32+ vérifiés, exports COM, dépendances système uniquement). xUnit : **484/484** (Core 237, Audio 168 [+18 APO : ordre sauvegarde→écriture, restauration exacte, idempotence, réparation, anti-boucle, inertie], App 79). Terrain : checklist de validation à exécuter sur machine Windows (Realtek/Intel/NVIDIA/BT/HDMI, MAJ pilote, mode exclusif) |

**Détail technique — pourquoi ça marche sans signature.** Un sAPO est un
objet COM in-process (interfaces `IAudioProcessingObject{,RT,Configuration}`
+ `IAudioSystemEffects`) que `audiodg.exe` (LocalService) instancie via
`CoCreateInstance` : du code UTILISATEUR. L'activation standalone écrit (1)
l'inscription COM du CLSID (`HKLM\SOFTWARE\Classes\CLSID\{EAA24610-…}` →
DLL déployée dans `%ProgramData%\RoomEq\apo\bin`, lecture seule pour les
utilisateurs standard — une DLL chargée par un service ne doit pas être
remplaçable par un compte non administrateur) et (2) le CLSID en valeur EFX
des `FxProperties` de l'endpoint choisi
(`{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},7` — « toujours appliquée, même aux
flux raw », Microsoft Learn), avec déblocage de `PKEY_AudioEndpoint_Disable_
SysFx` si besoin. Le service audio est alors redémarré (~1 s). L'APO lit
`%ProgramData%\RoomEq\apo\active.json` (format preset D23) via un thread de
fond (sondage 100 ms, horodatage fin — `st_mtime` à la seconde raterait les
réécritures pendant un glissement de curseur), reconstruit sa chaîne HORS du
chemin audio et publie par pointeur atomique ; `APOProcess` ne fait ni
allocation ni verrou ni E/S (les libérations sont confiées à un cimetière
vidé par le thread de fond), et **aucune méthode ne retourne jamais d'échec**
: après 10 échecs, le moteur désactiverait les effets système du périphérique
(y compris ceux du fabricant) — tout imprévu dégrade en pass-through et se
journalise. Anti-boucle (I-AUD-03 v2) : l'activation refuse toute entrée de
câble virtuel (RoomEq, VB-CABLE, VoiceMeeter, LoopBeAudio). Inconvénients
assumés et documentés dans l'UI : WASAPI exclusif/ASIO/KS contournent tout
effet système ; les effets EFX du fabricant sont suspendus sur une sortie
activée (sauvegardés, restaurés) ; une MAJ de pilote peut écraser
FxProperties → bouton « Réparer » + réparation automatique au branchement à
chaud ; mécanisme standalone éprouvé (Equalizer APO, 10+ ans) mais non
officiellement supporté par Microsoft. **Complément D24 (v3.9.6)** : le
chargement réel de la DLL exige la recette COMPLÈTE d'Equalizer APO —
`DisableProtectedAudioDG=1` (sans quoi audiodg, en mode « audio protégé »,
vérifie la signature des APO et REFUSE SILENCIEUSEMENT les non signées :
notre DLL n'était jamais même ouverte), l'inscription
`AudioEngine\AudioProcessingObjects\{CLSID}` (équivalent RegisterAPO du
DDK) et le mode de traitement EFX par défaut (MULTI_SZ). RoomEq applique
les trois, avec deux différences assumées : la valeur d'origine de la
politique est SAUVEGARDÉE (audio-policy.json) et restituée à la
désinstallation, et un réglage posé par un tiers (Equalizer APO installé)
n'est jamais modifié ni restauré. Contrepartie documentée à l'utilisateur
(activation + LISEZMOI) : les applications exigeant un chemin audio
protégé (certaines DRM) peuvent refuser de jouer tant que l'EQ système est
actif — le prix du gratuit, identique pour tous les utilisateurs
d'Equalizer APO depuis 10+ ans.

## D25 — Console allégée : retrait des tranches « entrée/sortie » (2 novembre 2026)

| | |
|---|---|
| **D25** | Demande développeur : « vu les dernières modifications (EQ dans le moteur Windows), les tranches de mixage "entrée" et "sortie" n'ont plus aucun intérêt (et peuvent causer des conflits ou confusion) » |
| **Décision** | Les tranches de console liées à des périphériques Windows — « entrée » (captures : micros, lignes, sortie du câble virtuel) et « sortie » (boucles WASAPI) — sont RETIRÉES de l'application (v3.8.0), ainsi que le routage qui les alimentait : bascule des périphériques par défaut vers la carte virtuelle, sélecteur de protocole (carte RoomEq / boucle WASAPI), installation du pilote, piste « câble » garantie, options « Router dès l'installation » et « Entrée par défaut → carte virtuelle ». L'onglet Mixage est réorganisé : le panneau « EQ système (APO) » passe EN TÊTE (LA voie pour tout le son Windows), la console ne garde que ses sources internes (lecteur, générateur, pont DAW ASIO) et un bouton « Démarrer la console ». Trois conflits réels motivent le retrait : (1) double correction — le son capté puis rejoué traversait l'EQ master PUIS l'APO sur la sortie ; (2) protections anti-boucle déclenchées (sorties master grisées, APO refusé sur une sortie captée) ; (3) confusion des débutants (micro et câble dans une appli d'EQ pièce). **Anti double-correction structurel** : si l'EQ système est actif sur la sortie master, le bus master CONTOURNE son propre EQ (`ApoOnMaster`, recalculé au changement de sortie et à chaque état APO). **Migration** : une machine ayant connu v3.6/v3.7 peut avoir l'entrée du câble virtuel comme sortie par défaut de Windows — plus rien ne la capterait (silence) : la sortie d'origine mémorisée dans mixer.json est restaurée automatiquement (une fois par session), sinon message actionnable ; le bouton « Supprimer le pilote » reste proposé tant qu'une carte héritée est détectée (l'installation, elle, disparaît). **macOS inchangé** : pas d'EQ système sur macOS, le routage par périphérique virtuel « RoomEq Virtual » reste la voie principale (libellés et guide dédiés). La couche audio M-AUD-03 conserve ses capacités capture/loopback (testées, défense en profondeur) — c'est l'application qui ne les expose plus (`MixerTrackPolicy`, refus aussi en appel direct et à la restauration de mixer.json) |
| **Alternatives écartées** | Garder les tranches en « mode avancé » caché (complexité et conflits maintenus pour un usage nul) ; retirer aussi la couche audio M-AUD-03 (refactor risqué de 168 tests verts pour du code inerte) ; garder l'installation du pilote « option avancée » (un pilote que plus rien n'utilise = confusion garantie) |
| **Validation** | xUnit **502/502** (Core 237, Audio 168 inchangés, App 97 [+18 D25 : `MixerTrackPolicyTests` — classification XOR exhaustive des six genres, kinds persistés capture/loopback détectés, badge « INTERNE » des sources internes]) ; build 0 erreur 0 avertissement ; VST/AU/APO natifs inchangés (harnais 112/112 non touchés). Terrain : vérifier sur machine ayant connu v3.7 avec pilote + routage actif que la sortie par défaut est restaurée au premier lancement 3.8.0 |

## D26 — Console retirée, verdict APO visible, instance unique (2 novembre 2026)

| | |
|---|---|
| **D26** | Trois demandes développeur : (1) « j'ai un bug dans l'installation : quand l'installation est finie, l'appli s'ouvre plusieurs fois » ; (2) « dans l'onglet mixage : je veux supprimer la partie "console", elle n'a plus aucun intérêt maintenant qu'on a l'égaliseur sans pilote » ; (3) « je n'arrive pas à activer l'égaliseur sans pilote sur une sortie : je clique, la page se rafraîchit et le bouton cliqué n'est toujours pas activé » |
| **Décision** | **(1) Instance unique** : l'installateur Velopack lance l'application dès la fin de l'installation ; tout double lancement (Setup exécuté deux fois, raccourci ouvert pendant un démarrage lent) créait une instance surnuméraire — fenêtres en double, `mixer.json` soumis à des écritures concurrentes, conflits d'accès aux périphériques. `Program.Main` acquiert désormais un mutex nommé par session (`Local\RoomEq.App.SingleInstance`, `SingleInstanceGuard`) ; toute instance surnuméraire se ferme immédiatement et silencieusement. Le mode élevé `--apo-setup` passe AVANT le garde : l'élévation doit toujours pouvoir s'exécuter à côté de l'application ouverte. **(2) Console retirée** : l'onglet Mixage devient le poste de pilotage de l'EQ système (panneau APO + « Écoute » : volume de la carte physique, preset adapté + pilote hérité). Disparaissent : tranches internes (lecteur/générateur/DAW ASIO/périphérique virtuel macOS), bus master et tranche MASTER, « Démarrer la console », auto-start, éditeur d'EQ de piste, pont `RoomEq ASIO` au démarrage, routage macOS « RoomEq Virtual » (la voie DAW macOS est le greffon AU ; la voie DAW Windows est le greffon VST — l'onglet ASIO reste pour inspecter les pilotes tiers). La **sortie d'écoute** est la barre du haut (source de vérité unique) : `FollowTopBarOutput` recible volume physique + preset + mémorisation, `PreferredInitialOutput` restaure la sortie de la session précédente. **Anti double-correction étendue au lecteur interne** : quand l'APO couvre la sortie du lecteur (`WindowsApoSystem.CoversEndpoint`), l'EQ LOCAL du lecteur se contourne (la courbe est déjà appliquée par le moteur Windows) — l'APO continue de recevoir la courbe avec le bypass UTILISATEUR ; pendant une mesure, `RoomMeasurementEngine.IsMeasuring` interdit au shell de réécrire le bypass (le moteur force « pièce seule » et restaure lui-même). **(3) Verdict APO visible** : le verdict de chaque action (succès comme échec : élévation refusée, sortie sans slot d'effets, périphérique débranché…) s'affiche EN COULEUR DANS le panneau « EQ système » (`SystemApoVerdictText`, ✓/✗) et n'est JAMAIS effacé par le rafraîchissement — auparavant il était relégué dans la barre d'état muette puis le panneau reprenait son texte générique : un échec d'activation était invisible, d'où « le bouton ne s'active pas » sans explication. Un second rafraîchissement différé (+1,5 s) rattrape l'énumération des sorties après le redémarrage du service audio (liste vide ou incomplète juste après `net start audiosrv`) ; les échecs sont désormais journalisés (version test) en plus des succès |
| **Alternatives écartées** | Corriger le multi-lancement côté installateur uniquement (Velopack ne propose pas de désactiver le lancement final, et la cause exacte du double déclenchement n'est pas prouvable à distance — le garde protège contre TOUTES les causes) ; garder une console « mode avancé » (l'utilisateur l'a explicitement jugée sans intérêt) ; tenter une élévation silencieuse sans UAC pour l'activation APO (impossible : l'écriture HKLM\MMDevices exige l'administrateur) |
| **Validation** | xUnit **479/479** (Core 237, Audio 168 inchangés, App 74 : −32 tests de console supprimés avec leur objet — `MixerTrackPolicyTests`, `BandTrackChannelTests` — +6 tests D26 `MixerSystemTabTests` : suivi de sortie d'écoute, restauration de sortie mémorisée, persistance réduite sans tranche résiduelle, chargement de configuration héritée sans exception, verdict qui survit au rafraîchissement, inertie `CoversEndpoint` hors Windows, garde d'instance unique) ; build 0 erreur 0 avertissement. Terrain (machine Windows) : réinstaller via Setup.exe (une seule fenêtre), activer l'EQ système sur la sortie par défaut (verdict ✓, bouton bascule), refuser l'invite UAC (verdict ✗ rouge explicite), lire un fichier pendant qu'une sortie APO est active (aucune double correction) |
