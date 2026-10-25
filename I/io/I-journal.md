# I-IO-02 — Journal de diagnostic

- **Module** : M-IO-02 · **Version** : 1 · **Code** : `src/RoomEq.Core/Io/DiagnosticsLog.cs`

## Intention
Tracer phases, formats et erreurs (piles complètes) dans un fichier par jour,
pour diagnostiquer à distance sans accès machine ; ne JAMAIS faire planter
l'application ni lever d'exception.

## Contrat
```csharp
static class DiagnosticsLog
    bool Enabled { get; set; } = true;   // public = false : plus RIEN (ni fichier, ni mémoire)
    string? LogPath { get; }
    void Info(string); void Warn(string); void Error(string, Exception? ex = null);
    string MemoryDump();                 // 600 dernières lignes
```

## Invariants
1. Fichier du jour `roomeq-AAAA-MM-JJ.log` (UTF-8, AutoFlush, append), bascule
   de fichier au changement de jour ; en-tête « # RoomEq — … » à la création.
2. Emplacements candidats : logs/ à côté de l'exécutable, puis
   %LOCALAPPDATA%\RoomEq\logs ; répertoire déjà échoué non retenté
   immédiatement ; tous inaccessibles → mémoire seule (aucune exception).
3. Lignes « HH:mm:ss.fff [NIVEAU] message » ; retours à la ligne du message
   remplacés par « ⏎ ».
4. Error avec exception : type, message, erreurs internes (préfixe « ↳ »),
   pile complète.
5. Enabled=false : ni fichier, ni tampon mémoire, aucune écriture (test dédié).
6. Thread-safe (lock) ; MemoryDump = jointure des lignes du tampon.

## Tests
Écrit dans un dossier temporaire injectable (fixture) : Info/Warn/Error y
apparaissent avec le format ; jour fictif → nouveau fichier ; Enabled=false →
aucune ligne nulle part ; Error avec exception imbriquée contient les trois
niveaux et la pile ; MemoryDump reflète les dernières lignes.
