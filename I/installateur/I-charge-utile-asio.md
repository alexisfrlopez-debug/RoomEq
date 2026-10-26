# I-INS-07 — Charge utile embarquée et installation ASIO

- **Module** : M-INS-07 · **Version de spec** : 1 · **Code** : futur — vague 8
  (`src/RoomEq.Installer/Payload.cs` + `AsioRegistration.cs`)
- **Référence scellée** : `roomeq-git-ref/installer/driver-setup/Program.cs`
  (`ExtractPayload`, `ExtractCertOnly`, `InstallAsio`, `UninstallAsio`,
  `AsioDllRegisteredPath`, v2.2.2) + `RoomEqDriverSetup.csproj` (ressources).

## Intention

Embarquer dans l'installateur autonome TOUT ce qu'il installe (un seul
fichier à distribuer, rien à télécharger), l'extraire vers un répertoire de
travail temporaire, et installer la DLL ASIO avec son enregistrement COM —
une étape NON bloquante : le pilote WASAPI fonctionne sans ASIO.

## Charge utile embarquée (5 ressources)

| Ressource | Rôle | Présence |
|---|---|---|
| `roomeqvad.inf` | INF du pilote (contrat M-INS-03) | toujours |
| `roomeqvad.sys` | pilote noyau (scellé N-DRV-01) | toujours |
| `roomeqvad.cat` | catalogue de signature | toujours |
| `RoomEqAsio64.dll` | DLL ASIO (scellée N-ASI-01) | toujours |
| `RoomEq-TestSigning.cer` | certificat de signature de test | **optionnelle** — sa PRÉSENCE définit le build signé-test (`TestSignedBuild`, M-INS-05) ; un build officiellement signé l'omets |

## Contrat public

```csharp
namespace RoomEq.Installer;

public sealed class Payload
{
    /// true si le build embarque le certificat de test (build signé-test).
    public static bool HasEmbeddedCert { get; }

    /// Nettoie puis recrée %TEMP%\RoomEqDriverSetup et y écrit les
    /// ressources. Retourne le dossier + le chemin du .cer (null si aucun).
    public (string WorkDir, string? CertPath) Extract();

    /// Extrait uniquement le certificat (désinstallation).
    public string? ExtractCertOnly();
}

public sealed class AsioRegistration
{
    public const string Clsid = "{8F5E3A91-4B2C-4D6E-9A1F-2E7C5B8D9A03}";
    public const string DllName = "RoomEqAsio64.dll";

    /// Copie la DLL vers %ProgramFiles%\RoomEq + clés HKLM.
    public string Install(string workDir);

    /// Retire clés + dossier (best-effort).
    public void Uninstall();

    /// Chemin enregistré (null = non enregistré) — sonde du diagnostic.
    public string? RegisteredPath();
}
```

## Invariants

1. **Ressource manquante = erreur explicite** (« ressource embarquée
   manquante : <nom> ») — jamais de fichier vide silencieusement écrit.
2. L'extraction **nettoie d'abord** le dossier de travail (best-effort)
   avant de le recréer : deux exécutions successives ne mélangent jamais
   deux versions du payload.
3. **Installation ASIO** (HKLM, élévation requise) :
   - copie de la DLL vers `%ProgramFiles%\RoomEq\RoomEqAsio64.dll`
     (écrasement autorisé — idempotent) ;
   - `HKLM\Software\ASIO\RoomEq ASIO` : `CLSID` = la constante,
     `Description` = « RoomEq ASIO (pont vers l'égaliseur RoomEq) » ;
   - `HKLM\Software\Classes\CLSID\{8F5E3A91-…}` : défaut = « RoomEq ASIO » ;
   - `…\InprocServer32` : défaut = chemin complet de la DLL copiée,
     `ThreadingModel` = `Apartment` — **identiques aux valeurs du MSI v2**
     (les deux canaux d'installation doivent rester interchangeables).
4. **CLSID constant verrouillé** : `{8F5E3A91-4B2C-4D6E-9A1F-2E7C5B8D9A03}`
   — c'est l'identité du pilote ASIO pour tous les DAW ; tout changement
   casse la détection par les hôtes (et doit être traité comme une
   décision, pas un refactor).
5. **Échec ASIO non bloquant** : avertissement « ASIO non installé » +
   « le pilote WASAPI fonctionne sans ASIO ; réessayer plus tard avec
   --install est sans risque (installation idempotente) ».
6. `RegisteredPath()` lit `InprocServer32` (défaut) — c'est la sonde du
   diagnostic et du panneau d'état.

## Critères de test

- extraction : dossier recréé vide avant écriture ; 4 fichiers toujours
  écrits ; 5e écrit si et seulement si `HasEmbeddedCert` ;
- `HasEmbeddedCert` = vrai ssi la ressource `.cer` existe (faux sur
  exception, pas de crash) ;
- ressources factices : contenu écrit identique octet pour octet ;
- registre factice (HKLM in-memory) : arbre exact des trois clés + valeurs
  (CLSID, Description, défaut, ThreadingModel=Apartment) ;
- `RegisteredPath` : clé absente → null ; présente → chemin exact ;
- désinstallation : clés et dossier retirés, exceptions absorbées ;
- échec de copie DLL → avertissement, PAS d'échec de séquence (exit 0).
