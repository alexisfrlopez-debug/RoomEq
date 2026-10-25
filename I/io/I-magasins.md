# I-IO-05 — Magasins de réglages nommés

- **Module** : M-IO-05 · **Version** : 1 · **Code** : `src/RoomEq.Core/Io/Stores.cs`

## Intention
Persistance JSON des presets EQ et profils micro : un fichier par élément,
sémantique partagée (liste/sauvegarde/chargement/suppression/renommage).

## Contrat
```csharp
static class AppPaths { DataDir; PresetsDir; MicProfilesDir; SanitizeFileName(name); }
static class JsonOpts { Default; }              // indented + énum en chaînes
abstract class JsonNamedStore<T>
    IReadOnlyList<string> List(); void Save(T); T? Load(string); void Delete(string);
    bool Rename(oldName, newName);
sealed class PresetStore : JsonNamedStore<EqPreset>      // (dir override pour tests)
sealed class MicProfileStore : JsonNamedStore<MicProfile>
```

## Invariants
1. Un fichier <nom sanitizé>.json par élément ; List = noms sans extension,
   tri insensible à la casse ; sauvegarde = sérialisation JsonOpts.
2. SanitizeFileName remplace tout caractère interdit de nom de fichier par « _ ».
3. Rename : charge l'élément, applique le renommage (délégué par type — le nom
   EST l'identité), sauvegarde sous le nouveau nom, supprime l'ancien ; faux si
   l'ancien n'existe pas ou si le nouveau existe déjà ; vrai sinon.
4. Load d'un nom absent → null (default) ; jamais d'exception sur Delete absent.
5. Répertoires créés à la construction (injectables pour les tests).

## Tests
Round-trip preset (bandes complètes) ; liste triée ; renommage (ancien
supprimé, nouveau chargé, contenu identique) ; renommage vers un nom existant
refusé ; caractères interdits sanitizés ; profils micro idem.
