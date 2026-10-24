<#
.SYNOPSIS
    Assemble le paquet MSIX RoomEq (cible Microsoft Store).

    Prérequis :
      • SDK .NET 8 (dotnet)
      • Windows SDK (makeappx.exe) — présent sur les runners GitHub Actions
        et sur tout poste avec Visual Studio / SDK Windows 10
      • paquet pilote + DLL ASIO : paramètres -DriverDir/-AsioDll (artefact
        CI) ou builds locaux (cf. build_msi.ps1)

    Étapes :
      1. dotnet publish src/RoomEq.App (win-x64, autonome) -> layout/
      2. copie des payloads : driver/ (donnée seulement — MSIX n'installe
         pas de pilote), RoomEqAsio64.dll, icônes, manifeste
      3. makeappx pack -> RoomEq-<version>-win-x64.msix (NON signé :
         la soumission Store est re-signée par Microsoft ; pour un test
         local, signer avec un certificat de test, cf. en-tête du manifeste)

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build_msix.ps1 `
      -DriverDir ..\ci-driver\driver -AsioDll ..\ci-driver\RoomEqAsio64.dll
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
# $PSScriptRoot = installer/msix/ → deux niveaux pour la racine du dépôt
$root  = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)   # RoomEq/
$inst  = $PSScriptRoot                             # installer/msix/
$build = Join-Path $inst 'build'
$layout = Join-Path $build 'layout'
New-Item -ItemType Directory -Force -Path $build, $layout | Out-Null

$version = (Select-Xml -Path (Join-Path $root 'Directory.Build.props') `
    -XPath '//Version').Node.InnerText.Trim()
Write-Host "==> RoomEq MSIX $version" -ForegroundColor Cyan

# ── 1) publication de l'application (auto-contenue, exécutable public) ──
$exe = Join-Path $layout 'RoomEq.exe'
if (-not $SkipPublish -or -not (Test-Path $exe)) {
    dotnet publish (Join-Path $root 'src\RoomEq.App') -c Release -r win-x64 `
        --self-contained -o $layout
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish a échoué.' }
}
# exécutable installé : RoomEq.exe (public, silencieux) ; RoomEq.App.exe est renommé
Copy-Item (Join-Path $layout 'RoomEq.App.exe') $exe -Force

# ── 2) payloads ───────────────────────────────────────────────────────────
# pilote : DONNÉE embarquée pour l'activation au premier lancement
# (un paquet MSIX ne peut pas installer de pilote noyau — cf. manifeste)
if ($DriverDir -eq '' -or -not (Test-Path (Join-Path $DriverDir 'roomeqvad.inf'))) {
    $local = Join-Path $root 'driver\roomeqvad\build\x64'
    if (Test-Path (Join-Path $local 'roomeqvad.inf')) { $DriverDir = $local }
    else { throw "Paquet pilote introuvable : compilez le pilote (driver\roomeqvad\build\build_driver.ps1) ou passez -DriverDir depuis l'artefact CI." }
}
$drvLayout = Join-Path $layout 'driver'
New-Item -ItemType Directory -Force -Path $drvLayout | Out-Null
Copy-Item (Join-Path $DriverDir '*') $drvLayout -Force
Write-Host "Payload pilote : $((Get-ChildItem $drvLayout).Count) fichiers"

# DLL ASIO : payload (l'enregistrement COM est différé au premier lancement
# ou à l'extension com:ComServer — cf. docs/SIGNATURE-ET-STORE.md § ASIO)
if ($AsioDll -eq '' -or -not (Test-Path $AsioDll)) {
    $local = Get-ChildItem -Path (Join-Path $root 'asio\asiodll') -Recurse -Filter 'RoomEqAsio64.dll' `
        -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($local) { $AsioDll = $local.FullName }
    else { throw "RoomEqAsio64.dll introuvable : compilez asio\asiodll\RoomEqAsio.vcxproj (MSVC x64) ou passez -AsioDll." }
}
Copy-Item $AsioDll $layout -Force

# icônes + manifeste (makeappx exige le nom exact « AppxManifest.xml »)
$assetsLayout = Join-Path $layout 'assets'
New-Item -ItemType Directory -Force -Path $assetsLayout | Out-Null
Copy-Item (Join-Path $inst 'assets\*.png') $assetsLayout -Force
Copy-Item (Join-Path $inst 'Package.appxmanifest') (Join-Path $layout 'AppxManifest.xml') -Force

# ── 3) makeappx ───────────────────────────────────────────────────────────
$makeappx = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Recurse `
    -Filter 'makeappx.exe' -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\x64\\' } |
    Sort-Object FullName -Descending | Select-Object -First 1
if (-not $makeappx) { throw 'makeappx.exe introuvable (Windows SDK requis).' }
Write-Host "makeappx : $($makeappx.FullName)"

$msix = Join-Path $build "RoomEq-$version-win-x64.msix"
& $makeappx.FullName pack /o /v /d $layout /p $msix
if ($LASTEXITCODE -ne 0) { throw 'makeappx pack a échoué.' }

$info = Get-Item $msix
Write-Host "==> MSIX généré : $($info.FullName) ($($info.Length) octets)" -ForegroundColor Green
