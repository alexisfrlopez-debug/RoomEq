<#
.SYNOPSIS
    Assemble la distribution « executables » RoomEq (sans MSI) :
    application portable + setup applicatif + setup pilote.

.DESCRIPTION
    Produit dans installer\build\ :
      - RoomEq-<version>-win-x64.exe  : application Avalonia PORTABLE,
        auto-contenue en un seul fichier (aucune installation, aucun UAC,
        executable depuis n'importe quel dossier) ;
      - RoomEqDriverSetup-<version>.exe : installateur de la carte son
        virtuelle + ASIO en un seul fichier (les fichiers pilote sont
        embarques en ressource ; une invite UAC, sortie console detaillee,
        --status / --uninstall disponibles) ;
      - RoomEqAppSetup-<version>.exe : version INSTALLABLE de l'application
        (l'exe portable est embarque en ressource). Installation par
        utilisateur dans %LOCALAPPDATA%\Programs\RoomEq sans UAC : raccourcis
        Menu Demarrer/Bureau, lancement automatique au demarrage de Windows
        (cle HKCU Run), entree « Applications & fonctionnalites » avec
        desinstalleur ; --status / --uninstall disponibles.

    Prerequis :
      - SDK .NET 8 (dotnet)
      - paquet pilote (roomeqvad.inf/.sys/.cat + .cer de test) et
        RoomEqAsio64.dll : soit compilés localement (build_driver.ps1,
        asio\asiodll\RoomEqAsio.vcxproj), soit passes depuis l'artefact CI.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build_exes.ps1 `
        -DriverDir ..\..\ci-driver\driver -AsioDll ..\..\ci-driver\RoomEqAsio64.dll
#>
[CmdletBinding()]
param(
    # dossier contenant roomeqvad.inf/.sys/.cat (+ .cer) - build local ou artefact CI
    [string]$DriverDir = "",
    # DLL ASIO compilee (MSVC x64)
    [string]$AsioDll = ""
)

$ErrorActionPreference = 'Stop'
$root  = Split-Path -Parent $PSScriptRoot          # racine du depot
$build = Join-Path $PSScriptRoot 'build'
New-Item -ItemType Directory -Force -Path $build | Out-Null

$version = (Select-Xml -Path (Join-Path $root 'Directory.Build.props') `
    -XPath '//Version').Node.InnerText.Trim()
Write-Host "==> RoomEq executables $version" -ForegroundColor Cyan

# ── 1) application portable (fichier unique auto-contenu) ──────────────────
$portable = Join-Path $build 'portable'
dotnet publish (Join-Path $root 'src\RoomEq.App') -c Release -r win-x64 `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $portable
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish (application portable) a echoue.' }

$appExe = Join-Path $build "RoomEq-$version-win-x64.exe"
Copy-Item (Join-Path $portable 'RoomEq.App.exe') $appExe -Force
Write-Host "    application : $appExe ($([Math]::Round((Get-Item $appExe).Length / 1MB, 1)) Mo)" -ForegroundColor Green

# ── 2) resolution des sources pilote / ASIO ─────────────────────────────────
if ($DriverDir -eq '' -or -not (Test-Path (Join-Path $DriverDir 'roomeqvad.inf'))) {
    $local = Join-Path $root 'driver\roomeqvad\build\x64'
    if (Test-Path (Join-Path $local 'roomeqvad.inf')) { $DriverDir = $local }
    else { throw "Paquet pilote introuvable : compilez (driver\roomeqvad\build\build_driver.ps1) ou passez -DriverDir (artefact CI)." }
}
if ($AsioDll -eq '' -or -not (Test-Path $AsioDll)) {
    $local = Get-ChildItem -Path (Join-Path $root 'asio\asiodll') -Recurse -Filter 'RoomEqAsio64.dll' `
        -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($local) { $AsioDll = $local.FullName }
    else { throw "RoomEqAsio64.dll introuvable : compilez asio\asiodll\RoomEqAsio.vcxproj (MSVC x64) ou passez -AsioDll." }
}

# ── 3) payload embarque du setup pilote ─────────────────────────────────────
$proj    = Join-Path $PSScriptRoot 'driver-setup'
$payload = Join-Path $proj 'payload'
New-Item -ItemType Directory -Force -Path $payload | Out-Null
Get-ChildItem $payload -File -ErrorAction SilentlyContinue | Remove-Item -Force
foreach ($f in @('roomeqvad.inf', 'roomeqvad.sys', 'roomeqvad.cat')) {
    $src = Join-Path $DriverDir $f
    if (-not (Test-Path $src)) { throw "$f introuvable dans $DriverDir" }
    Copy-Item $src $payload -Force
}
# certificat de test : optionnel (build officiellement signe = absent)
$cer = Get-ChildItem $DriverDir -Filter '*.cer' | Select-Object -First 1
if ($cer) { Copy-Item $cer.FullName (Join-Path $payload 'RoomEq-TestSigning.cer') -Force }
Copy-Item $AsioDll (Join-Path $payload 'RoomEqAsio64.dll') -Force
Write-Host "    payload setup : $( (Get-ChildItem $payload).Count ) fichiers (certificat : $(if ($cer) {'present'} else {'ABSENT -> build officiel'}))"

# ── 4) compilation du setup pilote ──────────────────────────────────────────
dotnet build $proj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Compilation de RoomEqDriverSetup a echoue.' }

$setupSrc = Join-Path $proj 'bin\Release\net472\RoomEqDriverSetup.exe'
if (-not (Test-Path $setupSrc)) { throw "RoomEqDriverSetup.exe introuvable apres build : $setupSrc" }
$setupExe = Join-Path $build "RoomEqDriverSetup-$version.exe"
Copy-Item $setupSrc $setupExe -Force
Write-Host "    setup pilote : $setupExe ($([Math]::Round((Get-Item $setupExe).Length / 1KB)) Ko)" -ForegroundColor Green

# ── 5) version installable de l'application (payload = exe portable) ───────
$appProj    = Join-Path $PSScriptRoot 'app-setup'
$appPayload = Join-Path $appProj 'payload'
New-Item -ItemType Directory -Force -Path $appPayload | Out-Null
Get-ChildItem $appPayload -File -ErrorAction SilentlyContinue | Remove-Item -Force
Copy-Item $appExe (Join-Path $appPayload 'RoomEq-portable.exe') -Force
# v2.1.0 : le setup pilote est aussi embarque (le bouton « Installer le pilote »
# de l'onglet Mixage le lance ; installation complete avec creation du peripherique)
Copy-Item $setupExe (Join-Path $appPayload 'RoomEqDriverSetup.exe') -Force

dotnet build $appProj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Compilation de RoomEqAppSetup a echoue.' }

$appSetupSrc = Join-Path $appProj 'bin\Release\net472\RoomEqAppSetup.exe'
if (-not (Test-Path $appSetupSrc)) { throw "RoomEqAppSetup.exe introuvable apres build : $appSetupSrc" }
$appSetupExe = Join-Path $build "RoomEqAppSetup-$version.exe"
Copy-Item $appSetupSrc $appSetupExe -Force
if ((Get-Item $appSetupExe).Length -lt (Get-Item $appExe).Length) {
    throw 'RoomEqAppSetup.exe semble ne pas embarquer l application portable (taille < payload).'
}
Write-Host "    setup appli  : $appSetupExe ($([Math]::Round((Get-Item $appSetupExe).Length / 1MB, 1)) Mo)" -ForegroundColor Green

Write-Host "==> Distribution prete dans $build (app portable + setup appli + setup pilote)" -ForegroundColor Green
