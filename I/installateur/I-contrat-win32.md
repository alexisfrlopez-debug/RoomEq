# I-INS-02 — Contrat Win32 SetupAPI / newdev / cfgmgr32

- **Module** : M-INS-02 · **Version de spec** : 1 · **Code** : futur — vague 8
  (`src/RoomEq.Installer/Win32Pnp.cs`)
- **Référence scellée** : `roomeq-git-ref/installer/driver-setup/Program.cs`
  (classe `NativeMethods` + méthodes PnP, v2.2.2) — recette devcon.

> Ce module porte le contrat interop NATIF. L'erreur 0x6F8 de la v2.2.1
> (structure mal déclarée, présente depuis le premier portage, dupliquée dans
> DEUX copies du code) est exactement la classe de défaut que cette spec doit
> rendre impossible : la taille de structure devient un invariant TESTÉ, pas
> une conséquence de la déclaration.

## Intention

Encapsuler une seule fois l'interopérabilité Win32 nécessaire à la création,
la sonde et le retrait du périphérique racine `ROOT\ROOMEQVAD`, avec des
déclarations EXACTES vérifiées par tests. Aucun autre module n'appelle
SetupAPI/newdev/cfgmgr32 directement (suppression de la duplication
installateur console / custom action MSI / application).

## Contrat public

```csharp
namespace RoomEq.Installer;

public sealed class Win32Pnp
{
    /// Le périphérique ROOT\ROOMEQVAD existe-t-il ? (+ statut/problème PnP)
    public bool DeviceExists(out DevNodeStatus st);

    /// Crée le périphérique racine s'il manque, lie le pilote, retourne
    /// true si l'appel a créé le nœud (false = déjà présent).
    public bool EnsureVirtualDevice(string infPath);

    /// Retrait du nœud via DIF_REMOVE. false = introuvable.
    public bool RemoveRootDevice();

    /// Identifiant d'instance (ex. ROOT\ROOMEQVAD\0000).
    public string? DeviceInstanceId();
}

public readonly record struct DevNodeStatus(uint Status, uint Problem)
{
    public bool Started     => (Status & 0x00000008) != 0; // DN_STARTED
    public bool HasProblem  => (Status & 0x00000400) != 0; // DN_HAS_PROBLEM
}
```

## Déclarations Win32 (contrat EXACT)

| Fonction | DLL | Rôle |
|---|---|---|
| `SetupDiGetClassDevsW(null, "ROOT", 0, DIGCF_PRESENT\|DIGCF_ALLCLASSES)` | setupapi | énumération des nœuds racine présents |
| `SetupDiEnumDeviceInfo(set, i, ref dev)` | setupapi | parcours ; fin = break (pas une erreur) |
| `SetupDiGetINFClassW(inf, out guid, name, 64, out needed)` | setupapi | classe lue dans l'INF ; **échec toléré** → repli classe MEDIA + message `[!]`, jamais d'échec silencieux (leçon v2.2.1 : l'erreur résiduelle 0xE0000205 polluait ensuite les appels de création) |
| `SetupDiCreateDeviceInfoList(ref classGuid, 0)` | setupapi | jeu de travail |
| `SetupDiCreateDeviceInfoW(set, deviceName, ref classGuid, null, 0, DICD_GENERATE_ID, ref dev)` | setupapi | **`deviceName` = `ROOMEQVAD` SANS le préfixe `ROOT\`** : DICD_GENERATE_ID fabrique lui-même `ROOT\<nom>\NNNN` ; passer le préfixe donne `ROOT\ROOT\ROOMEQVAD\NNNN` et échoue selon les builds |
| `SetupDiSetDeviceRegistryPropertyW(set, ref dev, SPDRP_HARDWAREID, bytes, len)` | setupapi | HardwareId = `ROOT\ROOMEQVAD` en **UTF-16 avec double NUL terminal** |
| `SetupDiCallClassInstaller(DIF_REGISTERDEVICE, …)` | setupapi | enregistrement du nœud |
| `SetupDiCallClassInstaller(DIF_REMOVE, …)` | setupapi | retrait (désinstallation) |
| `UpdateDriverForPlugAndPlayDevicesW(0, "ROOT\\ROOMEQVAD", inf, INSTALLFLAG_FORCE\|INSTALLFLAG_NONINTERACTIVE, out reboot)` | newdev | liaison du pilote au HardwareId |
| `CM_Get_Device_IDW(devInst, sb, 260, 0)` | cfgmgr32 | identification des nœuds énumérés |
| `CM_Get_DevNode_Status(out st, out pb, devInst, 0)` | cfgmgr32 | DN_STARTED / DN_HAS_PROBLEM + code problème |

## Invariants (verrous de régression)

1. **`SP_DEVINFO_DATA` : `cbSize` + `ClassGuid` + `DevInst` + `Reserved`
   (ULONG_PTR → `IntPtr`)** — `Marshal.SizeOf<SP_DEVINFO_DATA>() == 32` sur
   x64. La déclaration fautive d'origine (champ `DeviceDescription`
   `ByValTStr(200)` à la place de `Reserved` → cbSize 424) provoquait
   ERROR_INVALID_USER_BUFFER 0x6F8 sur TOUTES les fonctions SetupDi
   (v2.2.1 : échec étape 5 + faux « Périphérique : Absent »).
2. Toute erreur Win32 est rapportée avec `GetLastWin32Error()` en hexadécimal
   dans le message (`erreur 0x…`) — le numéro d'étape ET le code sont les deux
   informations de diagnostic demandées à l'utilisateur.
3. Chaque `SetupDiGetClassDevs` a son `SetupDiDestroyDeviceInfoList` dans un
   `finally` (pas de fuite de handles sur chemin d'erreur).
4. Recherche du nœud : énumération + `CM_Get_Device_IDW` dont l'id commence
   par `ROOT\ROOMEQVAD` (insensible à la casse) — jamais par description.
5. Codes de problème PnP traduits en libellés français pour le VERDICT :
   10, 28, 31, 37, 39, 43, 45, 52 (52 = « signature numérique non vérifiable
   (pilote signé-test refusé en mode normal) » — le cas signature le plus
   fréquent).

## Critères de test

- `Marshal.SizeOf<SP_DEVINFO_DATA>() == 32` (x64) — **verrou 0x6F8** ;
- bytes HardwareId : `"ROOT\ROOMEQVAD"` encodé UTF-16 + `"\0\0"`, longueur
  paire, transmis tel quel à SPDRP_HARDWAREID ;
- `deviceName` passé à `SetupDiCreateDeviceInfoW` ne commence JAMAIS par
  `ROOT\` (verrou v2.2.1 « ROOT\ROOT\… ») ;
- échec `SetupDiGetINFClassW` → repli classe MEDIA signalé, séquence continue ;
- dossier de travail détruit sur tous les chemins (mock comptant les appels) ;
- libellés PnP : table 10/28/31/37/39/43/45/52 complète + repli « code PnP N ».
