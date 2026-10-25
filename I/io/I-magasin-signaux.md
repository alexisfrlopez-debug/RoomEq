# I-IO-04 — Catalogue de signaux téléchargeables

- **Module** : M-IO-04 · **Version** : 1 · **Code** : `Io/SignalPack.cs` + `Io/SignalStore.cs` + `Io/SignalCatalogClient.cs`

## Intention
Signaux de test additionnels hébergés sur le dépôt GitHub (dossier signaux/) :
manifeste validé AVANT tout contact réseau, téléchargements bornés, magasin
local vérifié par SHA-256.

## Contrat
```csharp
record DownloadableSignalItem(Id, DisplayName, Description, FileName, Bytes, Sha256);
static class SignalManifest
    bool TryParse(string json, out List<DownloadableSignalItem>, out string error);
static class SignalUrls
    BaseDir; ManifestUrl; FileUrl(fileName); bool IsAllowedFileUrl(url);
sealed class SignalStore(string? overrideDir = null)
    IReadOnlyList<DownloadedSignalFile> List();  // (FileName, DisplayName, Bytes)
    string PathFor(fileName); bool Contains(fileName, expectedBytes);
    bool Save(fileName, content, expectedSha256, displayName);
sealed class SignalCatalogClient
    Task<(List? Signals, string? Error)> FetchManifestAsync(ct);
    Task<(byte[]? Content, string? Error)> DownloadFileAsync(item, progress?, ct);
```

## Invariants
1. Manifeste : ≤ 50 signaux, ≥ 1 ; id `[a-z0-9][a-z0-9-]{0,63}` unique ;
   displayName 1..80 ; description ≤ 300 ; fileName `[A-Za-z0-9][A-Za-z0-9._-]{0,127}`
   finissant .wav, unique (casse ignorée) ; bytes 1..25 Mo ; sha256 hex 64
   minuscules ; toute violation → erreur explicite en français.
2. URLs verrouillées : https, host exact raw.githubusercontent.com, chemin
   exact /alexisfrlopez-debug/RoomEq/main/signaux/, ni query ni fragment ni «..».
3. Téléchargement : redirections interdites ; Content-Length contrôlé ET taille
   imposée au fil de l'eau (interruption au dépassement) ; manifeste ≤ 512 Ko ;
   taille finale comparée à l'annoncée ; aucune exception vers l'UI (erreurs
   en clair) ; annulation propre.
4. Magasin : dossier %APPDATA%/RoomEq/signaux (injectable) ; Save refuse tout
   nom non sûr (sanitization, séparateurs, point initial, extension) et toute
   taille hors 1..25 Mo ; SHA-256 vérifiée AVANT écriture (mismatch →
   InvalidDataException) ; index.json des noms conviviaux (corruption tolérée :
   repli sur le nom de fichier) ; List trié, robuste aux dossiers illisibles.

## Tests
TryParse : manifeste valide ; chaque règle violée individuellement (12 cas) ;
URLs autorisées/refusées (9 cas dont https→http, autre dépôt, ../, query) ;
Store : Save + List ordonné + Contains ; index corrompu → repli ; nom
dangereux → exception ; SHA incorrecte → exception ; taille nulle → exception.
