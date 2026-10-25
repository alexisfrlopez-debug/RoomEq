# I-IO-01 — Saveur d'application (test / public)

- **Module** : M-IO-01 · **Version** : 1 · **Code** : `src/RoomEq.Core/Io/AppFlavor.cs`

## Intention
Un seul binaire, deux comportements : la version publique est SILENCIEUSE par
défaut (respect de la vie privée) ; seuls un nom d'exécutable contenant « test »
ou la variable d'environnement ROOMEQ_TEST=1 activent journal et télémétrie.

## Contrat
```csharp
enum AppFlavorKind { Test, Public }
static class AppFlavor
    AppFlavorKind Current { get; }              // figé au chargement
    bool IsPublic; bool IsTest; string DisplayName;   // "test" | "public"
    string PublicExePath { get; }               // RoomEq.exe voisin
    bool PublicExeAvailable;
    static AppFlavorKind FromExecutableName(string pathOrName);
```

## Invariants
1. FromExecutableName : nom de fichier (sans chemin) contenant « test »
   (casse ignorée) → Test ; sinon ROOMEQ_TEST=1 → Test ; sinon Public.
2. Environment illisible ou ProcessPath absent → Public (le silence est le
   comportement sûr). La détection n'échoue jamais.
3. PublicExePath = BaseDirectory/RoomEq.exe ; jamais d'exception.

## Tests
FromExecutableName : « RoomEq-test.exe »→Test ; « RoomEq.exe »/« RoomEq.App.exe »/
« public.exe »→Public ; variable ROOMEQ_TEST=1→Test (puis nettoyée) ;
DisplayName cohérent ; chemins sans exception.
