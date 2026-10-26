# JOURNAL D'OPTIMISATION — RoomEq v3

> Registre normalisé par le protocole TRIADE v0.4.0 (règle R11, décision D21 du dépôt
> triade) : toute optimisation exige un hot path identifié par mesure, une mesure
> avant/après consignée et un contrat stable (R4). L'optimisation prématurée — sans hot
> path ni mesure — est interdite : sa demande est refusée et consignée ici. Versionné avec
> la couche I du dépôt.

---

[OPT-01] 26 octobre 2026 · chaîne d'égalisation (EqChain, thread audio) et chemins
associés (RingBuffer SPSC, régénération ESS, FFT) · développeur (demande) / agent
(mesure)

- **hot path** : fil audio temps réel — 48 kHz stéréo, blocs de 10 ms ; identification par
  harnais de mesure dédié (`perf/`, décision D15 de ce dépôt), best-of-5 sur 2 vCPU
  partagés.
- **mesure avant** (état cristallisé v3.0.0-dev.1, i.e. sans aucune optimisation
  supplémentaire) :
  - EqChain 30 bandes × 2 canaux : **66 µs/bloc → 152× le temps réel** ;
  - allocations : **0 octet sur 100 000 blocs consécutifs** (1 000 s de son continu) ;
  - RingBuffer SPSC : 0,18 µs/bloc → 56 461× ;
  - ESS 12 s régénéré en 10,7 ms → 1 121× ;
  - FFT 4 096 points : 165 µs (517×) ; FFT 1 048 576 points : 38,2 ms (572×).
- **changement** : aucun — demande d'« optimisation maximale » (fusion des modules et
  réécriture « en natif machine ») examinée puis refusée · contrat stable : sans objet
  (aucun changement).
- **mesure après** : identique à la mesure avant (aucune modification de `src/`).
- **gain** : nul, par absence de problème mesuré — le budget temps réel est déjà dépensé
  à 152× de marge et l'allocation est nulle · **décision : refusée** (motif : aucun hot
  path déficient ; la fusion détruirait les adresses stables et les specs par
  responsabilité sans gain d'exécution ; la réécriture native est un recodage v4 sans
  besoin mesuré — contraire à la fonction objectif).
- **décision liée** : D15 (ce dépôt) ; R11 et D21 (protocole v0.4.0).

**Conditions de réexamen** (conservées de D15) : SIMD sur FFT/biquads uniquement si un
profilage montre un goulot sur un scénario utilisateur réel ; NativeAOT uniquement si un
besoin de démarrage mesuré apparaît. Toute réouverture passera par une nouvelle entrée
OPT-NN avec mesure avant/après.
