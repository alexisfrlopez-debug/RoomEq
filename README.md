# RoomEq v3 — recodage sous protocole TRIADE

Égaliseur acoustique temps réel (correction de pièce + corrections casque AutoEq).
Ce dépôt contient le **recodage complet de l'application** (projets C#) sous la
gouvernance du protocole TRIADE : chaque module est défini par une **intention
diffable** (couche I, dossier `I/`), implémenté par du **code versionné**
(couche B, `src/` + `tests/`) et cartographié à **adresse stable** (couche C,
`CARTE.md`).

- **Base fonctionnelle** : v2.2.2 (`alexisfrlopez-debug/RoomEq` @ deff25c)
- **État** : RoomEq.Core recodé (24 modules, 212 tests verts) ; Audio et App à
  suivre — voir `docs/PLAN_RECODAGE.md`
- **Reprise** : lire `REPRISE.md` en premier
- **Décisions** : `docs/DECISIONS.md` (D1-D8) · **Carte** : `CARTE.md`
- **Natifs scellés** (pilote, ASIO, HAL macOS, installateurs) : `natif/REFERENCE-NATIF.md`

## Structure

```
I/          specs d'intention par module (une par module de la Carte)
src/        code source (RoomEq.Core ✓, RoomEq.Audio, RoomEq.App)
tests/      suites xUnit (contrat exécutable de la couche I)
docs/       décisions, plan de recodage
natif/      référence des composants natifs scellés v2.2.2
scripts/    outillage (sync_backup.sh : tests + commit + GitHub + Drive)
```

## Vérification

```bash
export PATH="$HOME/.dotnet:$PATH"
cd src && dotnet build RoomEq.sln -c Debug     # 0 erreur 0 avertissement
dotnet test ../tests/RoomEq.Core.Tests/RoomEq.Core.Tests.csproj   # 212 verts
```

Fonction objectif : **Sécurité > Performance > Optimisation > Coût en tokens**.
