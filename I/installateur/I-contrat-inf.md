# I-INS-03 — Contrat INF du pilote (roomeqvad.inx)

- **Module** : M-INS-03 · **Version de spec** : 1 · **Code** : futur — vague 8
  (`src/RoomEq.Installer/InfContract.cs` — validateur ; l'INF lui-même vit
  avec le pilote scellé N-DRV-01)
- **Référence scellée** :
  `roomeq-git-ref/driver/roomeqvad/roomeqvad/roomeqvad.inx` (v2.2.2).

> L'INF est un fichier de DONNÉES contractuel : pnputil l'accepte même quand
> il est invalide pour les APIs PnP — c'est l'erreur 0xE0000205 de la v2.2.0/v2.2.1
> (`Signature="$Windows NT$"` = style legacy NT4, accepté au magasin mais
> refusé par SetupDiGetINFClass et la création du périphérique). Le rôle de ce
> module est de rendre cette classe de défaut impossible à redistribuer :
> valider l'INF AVANT toute installation, au build ET à l'exécution.

## Intention

Définir le contrat de contenu de l'INF de la carte virtuelle et le module de
validation qui l'impose : tout écart est une erreur bloquante avec message
précis, jamais un avertissement passé sous silence.

## Contrat obligatoire du fichier INF

| Champ | Valeur exigée | Verrou |
|---|---|---|
| `Signature` | `"$CHICAGO$"` (INF_STYLE_WIN4, style du sample SYSVAD) | **interdit `"$Windows NT$"`** (legacy NT4 → 0xE0000205 sur SetupDiGetINFClass/création, alors que pnputil l'ajoute au magasin : défaut Invisible) |
| `Class` / `ClassGUID` | `MEDIA` / `{4d36e96c-e325-11ce-bfc1-08002be10318}` | cohérence avec le repli classe MEDIA de M-INS-02 |
| `Provider` | `RoomEq` | — |
| `DriverVer` | `MM/JJ/AAAA, N.N.N.N` — synchronisé avec la version du paquet | le validateur vérifie la présence et le format |
| `CatalogFile` | `roomeqvad.cat` (fichier présent dans le payload) | — |
| `PnpLockDown` | `1` | durcissement noyau |
| `[Manufacturer]` | `RoomEq,NTamd64.10.0...17763` | Windows 10 1809+ / 11, x64 uniquement |
| HardwareId installé | `%DeviceDesc%=RoomEqVAD, ROOT\ROOMEQVAD` | **doit correspondre à la constante `HwId` de M-INS-02** (`ROOT\ROOMEQVAD`) |
| Copie | `DestinationDirs` = 13 (DriverStore du paquet) + `CopyFiles` de `roomeqvad.sys` | — |
| Service | `AddService=roomeqvad,0x2,…` : SERVICE_KERNEL_DRIVER, SERVICE_DEMAND_START, `%13%\roomeqvad.sys` | le nom du service est celui sondé par `Status` (cf. M-INS-04) |
| Héritage audio | `Include=ks.inf,wdmaudio.inf` + `Needs=KS.Registration, WDMAUDIO.Registration` | sans cela : code 37/39 au démarrage |
| Endpoints | render « RoomEq Input » + capture « RoomEq Output » via GUIDs de noms de pin custom (`be4f1a3e…` / `d48deb08…`), `KSNODETYPE_ANY`, mode événementiel (`PKEY_AudioEndpoint_Supports_EventDriven_Mode = 1`) | **les libellés exacts des deux cartes** — l'app les recherche par ces noms |

## Contrat public du validateur

```csharp
namespace RoomEq.Installer;

public sealed class InfContract
{
    /// Valide un INF contre le contrat ci-dessus.
    /// Retourne la liste des écarts (vide = conforme).
    public static IReadOnlyList<InfViolation> Validate(string infPath);

    /// Extrait la classe d'installation déclarée (utilisée par M-INS-02).
    public static Guid? ReadClassGuid(string infPath);
}

public sealed record InfViolation(string Rule, string Detail, bool Blocking);
```

## Invariants

1. `Signature` non-WIN4 = violation **bloquante** — verrou 0xE0000205.
2. Toute rupture d'égalité croisée est bloquante : HardwareId de l'INF ↔
   `HwId` du code ↔ noms d'endpoints attendus par l'application v3.
3. `DriverVer` absent ou mal formé = violation bloquante (pnputil refuse
   silencieusement certains remplacements sans version).
4. Le validateur s'exécute : (a) au build (test de la fixture INF du paquet),
   (b) à l'étape 1 de l'installation (payload extrait) — avant tout appel pnputil.

## Critères de test

- fixture INF valide (copie du roomeqvad.inx v2.2.2) → zéro violation ;
- fixture mutée `Signature="$Windows NT$"` → violation bloquante mentionnant
  0xE0000205 dans le détail (le message explique le pourquoi) ;
- mutations : ClassGUID incohérent, HardwareId divergent, TargetVersion
  absent, DriverVer mal formé, endpoint renommé → chacune sa violation ;
- `ReadClassGuid` retourne le GUID de l'INF quand il est lisible ;
- l'étape 1 de M-INS-01 appelle le validateur (assertion d'ordre sur mocks).
