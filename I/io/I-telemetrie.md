# I-IO-03 — Télémétrie Drive (version test)

- **Module** : M-IO-03 · **Version** : 1 · **Code** : `Io/TelemetryConfig.cs` + `Io/DriveLogUploader.cs`

## Intention
L'EXE de test envoie ses journaux vers le dossier Drive de développement
(`<machine>-roomeq-<jour>.log`) pour diagnostic à distance. Aucune donnée audio.
Toujours silencieux côté appelant (messages d'état, jamais d'exception).

## Contrat
```csharp
sealed class TelemetryConfig { Enabled, ClientId, ClientSecret, RefreshToken,
    TokenUri = "https://oauth2.googleapis.com/token", FolderId;
    static Load(); static LoadIn(dir); }         // snake_case, jamais d'exception
static class DriveLogUploader
    TelemetryConfig Config { get; }              // public => Enabled=false forcé
    string LastResult { get; }  event Action<string> UploadFinished;
    static string DriveNameFor(machine, localFile);
    List<(string, string)> CollectLogFiles(machine, dirs?, includeMemoryFallback = true);
    Task<string> UploadNowAsync();
```

## Invariants
1. TelemetryConfig : chargée de telemetrie.json (BaseDirectory puis
   %LOCALAPPDATA%\RoomEq), clés snake_case ; absente/incomplète/illisible →
   Enabled=false (les 4 champs + Enabled doivent être complets).
2. DriveLogUploader.Config : version publique → toujours Enabled=false.
3. CollectLogFiles : jour + veille uniquement, noms roomeq-*.log, dédupliqués
   par nom Drive ; repli mémoire écrit en temporaire
   `roomeq-memoire-<jour>.log` si non vide (désactivable pour les tests).
4. DriveNameFor : machine sanitée (alphanumérique, -, _) + nom de fichier
   bas de casse ; DayFromName reconnaît AAAA-MM-JJ (10 caractères, 8 chiffres).
5. UploadNowAsync : désactivée → message court ; sinon refresh OAuth →
   recherche de l'ID Drive (q sur le nom + parent) → création multipart OU
   mise à jour PATCH (uploadType=media) ; échec par fichier non bloquant ;
   état final « N journal(aux) envoyé(s) » / raisons.
6. Journaux d'échec tracés via DiagnosticsLog.Warn — jamais d'exception vers
   l'appelant ; événement UploadFinished toujours déclenché après tentative.

## Tests
LoadIn : JSON complet → Enabled ; absent/incomplet/JSON cassé → désactivée ;
DayFromName/DriveNameFor/Sanitize (accents, espaces, vides) ; CollectLogFiles
sur dossier fictif : seulement aujourd'hui/veille, déduplication, repli
mémoire coupable ; version publique → Config.Enabled=false (via AppFlavor
figé — test du comportement de charge).
