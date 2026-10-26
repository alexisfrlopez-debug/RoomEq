# CONFORMITÉ TRIADE v0.4.0 — RoomEq v3

> Tableau de bord de conformité et analyse d'implications de la révision v0.4.0 du
> protocole (décisions D19–D25 du dépôt triade) pour ce dépôt. Établi le 26 octobre 2026,
> cristal `v3.0.2` (D14). Décision d'alignement : **D16**.

## 1. Tableau de bord de conformité

Métriques du § 15.2 de la spécification v0.4.0, instanciées pour une couche B
**source** (.NET 8 / C# / Avalonia) :

| Métrique | Cible | Valeur RoomEq | Source de mesure |
|---|---|---|---|
| Blocs avec spécification I à jour | 100 % | **42/42 modules applicatifs** + 8 specs installateur | `CARTE.md` (audit de traçabilité) |
| Chaînes de traçabilité complètes I↔C↔B↔tests | 100 % | **42/42** (spec, code, tests par module) | `CARTE.md` § Modules |
| Construction | 0 / 0 | **0 erreur, 0 avertissement** | `dotnet build` (R5) |
| Tests différentiels | 100 % verts | **437/437** (CI : Linux, Windows, macOS) | `dotnet test` + GitHub Actions |
| Empreintes des données embarquées | conformes | **2/2** — casques `ffff00a5…` (321 modèles), micros `f126f6f5…` (36) | md5 reproductible |
| Défauts ouverts par classe T1–T6 | 0 | **0 ouverts** ; historique classé : 5 défauts corrigés (voir § 3) | journal des décisions |
| Optimisations sans mesure (R11) | 0 | **0** — entrée OPT-01 : demande refusée après mesure | `docs/JOURNAL_OPTIMISATION.md` |
| Budgets tokens (40 / 3 000 / 6 000) | ≤ budgets | **cible** (instrumentation = mécanisme carte M1+, non applicable à l'instanciation source ; l'homologue mesuré est le coût de reprise par agent, non instrumenté) | — |
| Suite de conformité par profil actif | verte | **N/A au profil source** — mécanisme des profils natifs (M6/M7) ; homologue : CI 3 OS + simulateur d'empaquetage | GitHub Actions + `scripts/simulate_ci_packaging.sh` |
| Changements hors journal des décisions | 0 | **0** — 16 décisions (D1–D16), append-only | `docs/DECISIONS.md` |
| Pré-versions correctement balisées | 100 % | **conforme** — v3.0.0-dev.1 → v3.0.0 → v3.0.1 → v3.0.2, balises immuables, correctifs à contrat stable | tags git |

## 2. Implications de la v0.4.0 pour ce dépôt

La révision v0.4.0 du protocole est née des pratiques de ce dépôt : l'essentiel de
l'alignement est une **conformité rétroactive nommée**. Ce qui change concrètement :

1. **Le vocabulaire devient normatif.** Les « specs I écrites avant le code » sont
   désormais le gabarit normalisé § 2 (identifiants stables, cas d'échec, vecteurs,
   fixtures, provenance). Les 44 spécifications existantes sont conformes de fait
   (structure, ordre d'écriture, diffabilité) ; leurs identifiants `M-<DOM>-NN` dans la
   Carte correspondent à la clé de traçabilité exigée.
2. **La taxonomie T1–T6 classifie l'existant.** Les défauts historiques deviennent les
   occurrences de référence de la spécification (§ 3). Aucun recodage n'en découle —
   la classification est documentaire.
3. **La table des modules de la Carte devient officiellement la table de traçabilité**
   I↔C↔B↔tests (§ 4.2) : chaque ligne relie déjà spécification, code et tests ; la
   conformité est la lecture formelle d'un état existant.
4. **Le journal d'optimisation existe** (`docs/JOURNAL_OPTIMISATION.md`, OPT-01) : la
   mesure D15 y est consignée au format normalisé, avec ses conditions de réexamen.
5. **Les pré-versions deviennent un schéma** (dev.N / rc.N / final) : la séquence vécue
   v3.0.0-dev.1 → v3.0.2 en est l'occurrence de référence citée par la spécification
   elle-même. Toute version future suit le schéma (rc = CI verte + simulateur
   d'empaquetage verts).
6. **Les reverse-specs M-INS passent de pratique à mécanisme officiel** : leur statut
   « ⬜ spec prête » correspond au statut « prête » du § 10 ; la vague 8, si elle est
   lancée, partira de ces specs validées — c'était déjà la condition D11.

## 3. Classification taxonomique des défauts historiques

| Défaut (corrigé) | Classe | Verrou posé |
|---|---|---|
| Seuil de détection du balayage = 8, non justifié (bruit seul accepté, ratio ~186) | **T1 + T6** | constante `DetectionSnr = 1000` nommée + rationale ; spec I `I-analyse-balayage.md` ; test `PureNoise_IsRejected` |
| Structure `SP_DEVINFO_DATA` déclarée de mémoire (424 ≠ 32 octets, échec 0x6F8, 2 copies) | **T2** | spec I `I-contrat-win32.md` : invariant `Marshal.SizeOf == 32` testé |
| Sanitisation de noms de fichiers dépendante de l'OS hôte | **T3** | union fixe multi-plateformes + spec I `I-magasins.md` |
| Parsing de la sortie pnputil sur libellés localisés français (3 copies) | **T4 + T5** | spec I `I-contrat-outils.md` : fragments publi/orig, CP850, repli scan, **fixtures partagées** application ↔ installateur |
| Signature INF `$Windows NT$` au lieu de `$CHICAGO$` (échec 0xE0000205) | **T2** | spec I `I-contrat-inf.md` : validateur bloquant |

## 4. Ce qui ne s'applique pas à l'instanciation source (et pourquoi)

- **R10 renforcée (signature de manifeste, anti-rollback au chargement)** : mécanisme du
  profil natif, charge des jalons M0+. L'instanciation source en tient l'homologue
  disponible : historique git signé, balises immuables, empreintes md5 des données
  embarquées vérifiées à la livraison. La vérification cryptographique au chargement
  sera celle du binaire natif scellé (vague 8+ / M0+).
- **Suite de conformité par profil** : les profils de la spécification sont natifs
  (`<isa>-<os>-<format>`). L'homologue vérifié ici : intégration continue sur trois OS +
  reproducteur d'empaquetage exécuté avant toute balise.
- **Budgets tokens instrumentés** : la carte fragmentée est un mécanisme du protocole ;
  ce dépôt n'a pas de carte chiffrée. La discipline d'édition ciblée (jamais de lecture
  intégrale) a été pratiquée via la Carte et les specs par module — non instrumentée.

## 5. Conséquences pour la suite

- **Aucune modification de `src/`** n'est requise par la v0.4.0 (décision D16) : la
  conformité est gouvernance et documentation.
- La **vague 8** (installateur), si elle est lancée, part des reverse-specs M-INS —
  désormais officialisées — et devra suivre le schéma de pré-versions complet
  (`3.1.0-dev.N` → rc → finale selon la progression de contrat).
- Toute demande d'optimisation future passe par le journal (R11) : la charge de la preuve
  est sur le demandeur — mesure d'abord.
- Le jalon M0 du protocole demeure la prochaine étape de vérité côté triade ; ce dépôt
  reste la démonstration de référence de la couche de gouvernance.
