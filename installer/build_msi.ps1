<#
.SYNopsis
    Assemble l'installateur MSI RoomEq complet (Windows).

    Prérequis :
      • SDK .NET 8 (dotnet) + outil wix (dotnet tool install --global wix --version 6.0.2)
      • Visual Studio 2022 + WDK 10 pour le pilote (driver) et la DLL ASIO
        — OU passez -DriverDir/-AsioDll issus d'une artefact CI

    Étapes :
      1. dotnet publish src/RoomEq.App (win-x64, autonome)
      2. générer les fragments fichiers (gen_wix_fragment.py)
      3. compiler l'action personnalisée (RoomEq.Installer.CA, SfxCA)
      4. wix build -> RoomEq-<version>-win-x64.msi

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build_msi.ps1
    powershell -File build_msi.ps1 -DriverDir ..\driver\roomeqvad\build\x64 -AsioDll ..\asio\asiodll\x64\Release\RoomEqAsio64.dll
#>
[CmdletBinding()]
param(
    # dossier contenant roomeqvad.inf/.sys/.cat (sortie de build_driver.ps1 ou artefact CI)
    [string]$DriverDir = "",
    # DLL RoomEqAsio64.dll compilée (MSVC x64)
    [string]$AsioDll = "",
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$root  = Split-Path -Parent $PSScriptRoot          # racine du dépôt (RoomEq/)
$inst  = $PSScriptRoot
$build = Join-Path $inst 'build'
New-Item -ItemType Directory -Force -Path $build | Out-Null

$version = (Select-Xml -Path (Join-Path $root 'Directory.Build.props') `
    -XPath '//Version').Node.InnerText.Trim()
Write-Host "==> RoomEq MSI $version" -ForegroundColor Cyan

# ── 1) publication de l'application (auto-contenue, exécutable public) ──
$publish = Join-Path $build 'publish'
if (-not $SkipPublish -or -not (Test-Path (Join-Path $publish 'RoomEq.App.exe'))) {
    dotnet publish (Join-Path $root 'src\RoomEq.App') -c Release -r win-x64 `
        --self-contained -o $publish
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish a échoué.' }
}
# exécutable installé : RoomEq.exe (public, silencieux) ; RoomEq.App.exe est renommé
Copy-Item (Join-Path $publish 'RoomEq.App.exe') (Join-Path $publish 'RoomEq.exe') -Force

# ── 2) dossier pilote ────────────────────────────────────────────────────
if ($DriverDir -eq '' -or -not (Test-Path (Join-Path $DriverDir 'roomeqvad.inf'))) {
    $local = Join-Path $root 'driver\roomeqvad\build\x64'
    if (Test-Path (Join-Path $local 'roomeqvad.inf')) { $DriverDir = $local }
    else { throw "Paquet pilote introuvable : compilez le pilote (driver\roomeqvad\build\build_driver.ps1) ou passez -DriverDir depuis l'artefact CI." }
}

# ── 3) DLL ASIO ───────────────────────────────────────────────────────────
if ($AsioDll -eq '' -or -not (Test-Path $AsioDll)) {
    $local = Get-ChildItem -Path (Join-Path $root 'asio\asiodll') -Recurse -Filter 'RoomEqAsio64.dll' `
        -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($local) { $AsioDll = $local.FullName }
    else { throw "RoomEqAsio64.dll introuvable : compilez asio\asiodll\RoomEqAsio.vcxproj (MSVC x64) ou passez -AsioDll." }
}

# ── 4) fragments fichiers (équivalent multiplateforme de heat) ──────────
python (Join-Path $inst 'gen_wix_fragment.py') $publish AppFiles INSTALLDIR var.PublishRoot (Join-Path $build 'AppFiles.wxs')
python (Join-Path $inst 'gen_wix_fragment.py') $DriverDir DriverFiles DriverDir var.DriverRoot (Join-Path $build 'DriverFiles.wxs')
if ($LASTEXITCODE -ne 0) { throw 'gen_wix_fragment a échoué (python3 requis).' }

# ── 5) action personnalisée (SfxCA — étape Windows) ──────────────────────
$caProj = Join-Path $inst 'RoomEq.Installer.CA'
dotnet build $caProj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Compilation du CA a échoué.' }
$caDll = Get-ChildItem -Path (Join-Path $caProj 'obj') -Recurse -Filter 'RoomEq.Installer.CA.CA.dll' |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $caDll) { throw 'RoomEq.Installer.CA.CA.dll introuvable (MakeSfxCA doit tourner sous Windows).' }

# ── 6) liaison MSI ────────────────────────────────────────────────────────
$msi = Join-Path $build "RoomEq-$version-win-x64.msi"
# Extension UI : version 6.x PINNÉE — sans version, NuGet résout la
# dernière 7.0.0, incompatible avec wix 6 (WIX6101). L'ajout échoue
# silencieusement si l'extension est déjà enregistrée (cas CI) ; on
# vérifie donc sa présence via la liste des outils globaux.
wix extension add -g WixToolset.UI.wixext/6.0.2 2>$null | Out-Null
if (-not (dotnet tool list --global | Select-String 'wixtoolset.ui.wixext')) {
    throw "Extension WiX UI 6.0.2 requise : wix extension add -g WixToolset.UI.wixext/6.0.2"
}

wix build -arch x64 -culture fr-FR `
    -d "PublishRoot=$publish" -d "DriverRoot=$DriverDir" `
    -d "AsioDll=$AsioDll" -d "CADll=$($caDll.FullName)" -d "Version=$version" `
    (Join-Path $inst 'RoomEq.wxs') (Join-Path $build 'AppFiles.wxs') (Join-Path $build 'DriverFiles.wxs') `
    -o $msi
if ($LASTEXITCODE -ne 0) { throw 'wix build a échoué.' }

Write-Host "==> MSI généré : $msi" -ForegroundColor Green
