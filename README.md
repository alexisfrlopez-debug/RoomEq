# RoomEq v3 — recodage sous protocole TRIADE

Égaliseur acoustique temps réel (correction de pièce + corrections casque AutoEq).
Ce dépôt contient le **recodage complet de l'application** (projets C#) sous la
gouvernance du protocole TRIADE : chaque module est défini par une **intention
diffable** (couche I, dossier `I/`), implémenté par du **code versionné**
(couche B, `src/` + `tests/`) et cartographié à **adresse stable** (couche C,
`CARTE.md`).

- **Base fonctionnelle** : v2.2.2 (`alexisfrlopez-debug/RoomEq` @ deff25c)
- **État** : recodage **terminé, cristallisé et publié** au tag `v3.0.1`
  (v3.0.0 = cristallisation initiale à CI Windows rouge, non déplacée) —
  42 modules 🔒, 437 tests verts (CI 3 OS), parité v2.2.2 établie point par
  point (`docs/PARITE-v2.2.2.md`) ; les écarts terrain éventuels seront
  traités en patchs 3.0.x à contrat stable
- **Reprise** : lire `REPRISE.md` en premier
- **Décisions** : `docs/DECISIONS.md` (D1-D13) · **Carte** : `CARTE.md`
- **Natifs scellés** (pilote, ASIO, HAL macOS, installateurs) : `natif/REFERENCE-NATIF.md`

## Structure

```
I/          specs d'intention par module (une par module de la Carte)
src/        code source (RoomEq.Core + RoomEq.Audio + RoomEq.App)
tests/      suites xUnit (contrat exécutable de la couche I)
docs/       décisions, plan de recodage, audit de parité v2.2.2
natif/      référence des composants natifs scellés v2.2.2
scripts/    outillage (sync_backup.sh : tests + commit + GitHub + Drive ;
            package_v3.sh : livrables zips test/public/source)
```

## Vérification

```bash
export PATH="$HOME/.dotnet:$PATH"
cd src && dotnet build RoomEq.sln -c Debug     # 0 erreur 0 avertissement
dotnet test ../tests/RoomEq.Core.Tests/RoomEq.Core.Tests.csproj    # 220 verts
dotnet test ../tests/RoomEq.Audio.Tests/RoomEq.Audio.Tests.csproj  # 142 verts
dotnet test ../tests/RoomEq.App.Tests/RoomEq.App.Tests.csproj      # 75 verts
bash ../scripts/package_v3.sh                  # zips test/public/source
```

Fonction objectif : **Sécurité > Performance > Optimisation > Coût en tokens**.
