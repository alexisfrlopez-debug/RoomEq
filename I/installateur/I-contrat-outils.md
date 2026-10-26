# I-INS-04 — Contrat des outils externes (pnputil, sc, bcdedit)

- **Module** : M-INS-04 · **Version de spec** : 1 · **Code** : futur — vague 8
  (`src/RoomEq.Installer/ToolContract.cs`)
- **Référence scellée** : `roomeq-git-ref/installer/driver-setup/Program.cs`
  (`RunHidden`, `ParsePublishedName`, `ParseDriverStoreName`,
  `ScanWindowsInfFolder`, `IsTestSigningOn`, v2.2.2) — et, côté application,
  `src/RoomEq.Audio/Routing/DriverSetupManager.cs` (parsing dupliqué).

> Historique : le parsing de la sortie `pnputil` a créé des défauts sur TROIS
> versions (libellés accentués mal décodés v2.2.1, libellé réel Win11 FR
> « Nom d'origine » non matché v2.2.2) et vivait en TROIS copies (setup,
  custom action MSI, application). Ce module unifie le contrat : une seule
  implémentation, des fixtures partagées installateur ↔ application
  (M-AUD-03 `DriverSetupManager` devient consommateur de ce contrat).

## Intention

Encapsuler TOUTE invocation d'outil système externe (pnputil, sc, bcdedit)
derrière un contrat unique : lancement caché avec timeout, décodage de page de
code correct, tolérance de codes d'exit normaux, et parsing des sorties
tolérant à la langue — avec un repli indépendant des libellés quand le
parsing échoue.

## Contrat public

```csharp
namespace RoomEq.Installer;

public sealed class ToolContract
{
    /// Exécute un outil système : fenêtre cachée, sortie+erreur redirigées,
    /// décodage OEM CP850, timeout 120 s (kill au-delà), journalisation de la
    /// sortie brute uniquement si le code n'est ni 0 ni dans tolerated.
    public ToolResult Run(string exe, string args, params int[] tolerated);

    /// Résout le nom publié (oemNN.inf) de notre paquet dans le magasin.
    /// Stratégie : parse de /enum-drivers PUIS repli scan C:\Windows\INF.
    public string? ResolveStoreInfName(string originalInfName);

    /// bcdedit /enum {current} → testsigning actif ? (yes/oui)
    public bool IsTestSigningOn();
}

public sealed record ToolResult(int ExitCode, string Output, string Error);
```

## Invariants

1. **Décodage CP850** des sorties stdout/stderr (page OEM des consoles
   Windows, notamment en français). Le décodage .NET par défaut
   (Windows-1252) produit des mojibakes qui cassent le matching des clés
   accentuées — verrou v2.2.1.
2. **Timeout 120 s** par invocation, kill du processus au dépassement, puis
   attente de la mort du processus (pas de zombie).
3. **Codes d'exit normaux paramétrables** : `sc query` → 1060 (service non
   installé = état normal avant installation, pas une erreur) ;
   `pnputil /add-driver` → 259 (paquet ajouté, aucun périphérique ne
   correspond encore — normal, le nœud est créé après).
4. **Parsing `ParsePublishedName`** (sortie de `/add-driver`) : première
   ligne `clé: valeur` dont la clé contient le fragment `publi` — insensible
   à la langue et aux accents mal décodés (« Published Name », « nom du
   fichier publié », mojibake inclus).
5. **Parsing `ParseDriverStoreName`** (sortie de `/enum-drivers`) : blocs
   séparés par lignes vides (une clé `publi` reset le bloc) ; association
   `publi` + `orig` (fragment, car le libellé réel Win11 FR est « Nom
   d'origine », sans « INF » ni « Name » — verrou v2.2.2 : exiger plus
   donnait un faux « Pilote magasin : Absent » après un ajout réussi) ; la
   valeur `orig` doit égaler le nom INF d'origine, insensible à la casse.
6. **Repli indépendant de la langue** (`ScanWindowsInfFolder`) : si le parse
   ne trouve rien, balayer `C:\Windows\INF\oem*.inf` triés par date de
   modification décroissante et retourner le premier contenant l'aiguille
   `roomeqvad` en ASCII **ou** en UTF-16 — verrou v2.2.2 (les libellés
   pnputil sont traduits ET changeants selon les builds de Windows).
7. **bcdedit** : lecture de `/enum {current}` ; ligne contenant
   `testsigning` (insensible à la casse) → valeur finale `yes` ou `oui` =
   actif ; toute autre valeur ou ligne absente = inactif. Jamais d'exception
   sur sortie illisible → faux par défaut (prudence : ne jamais croire le
   mode test actif sur preuve faible).
8. Les arguments exacts sont constants du module :
   `/add-driver "<inf>" /install`, `/enum-drivers`,
   `/delete-driver <oem> /uninstall /force`, `query <service>`,
   `/enum {current}`, `/set testsigning on|off`,
   `/deletevalue {current} testsigning`.

## Critères de test

- fixtures de sortie pnputil : EN, FR (« Nom du fichier publié »), Win11 FR
  (« Nom d'origine »), FR mojibake CP850→1252, sortie sans libellé connu →
  repli ; chacune conduit au bon oemNN.inf ou au repli (verrous v2.2.1/v2.2.2) ;
- `ParseDriverStoreName` : ligne vide reset le bloc (pas d'association
  inter-blocs) ; `orig` sans `publi` précédent = null ;
- scan de repli : dossier factice avec oem9.inf (récent, contient
  « roomeqvad » en UTF-16) + oem8.inf (plus vieux, contient en ASCII) →
  retourne oem9.inf (ordre chronologique inverse) ;
- bcdedit : `testsigning Yes`, `testsigning Oui`, `testsigning No`, ligne
  absente, sortie vide → actif/actif/inactif/inactif/inactif ;
- `Run` : timeout déclenché (processus factice long) → kill + code d'échec ;
  code toléré → pas de journalisation de la sortie brute ;
- **fixtures partagées avec l'application** : `DriverSetupManager.ParseDriverStoreName`
  (M-AUD-03) passe les MÊMES fixtures — suppression contractuelle de la
  triple duplication historique.
