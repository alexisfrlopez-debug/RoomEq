<#
.SYNOPSIS
    Build the RoomEq Virtual Audio driver (roomeqvad) for Windows 10/11 x64.

.DESCRIPTION
    Requires Windows with Visual Studio 2022 + Windows Driver Kit (WDK) 10
    (matching Windows SDK). Produces, in driver\roomeqvad\build\x64\:
      - roomeqvad.sys    the kernel driver
      - roomeqvad.inf    the (stamped) INF
      - roomeqvad.cat    the catalog (via inf2cat)
    Then optionally test-signs them (see sign_driver_test.ps1).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build_driver.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [switch]$TestSign
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot   # driver\roomeqvad
$sln  = Join-Path $root 'roomeqvad.sln'
$out  = Join-Path $root "build\x64"

Write-Host '==> RoomEq Virtual Audio driver build' -ForegroundColor Cyan

# ── WDK/SDK via NuGet (voie officielle, aucune extension VS requise) ──
$pkgDir = Join-Path $root 'packages'
$wdkInstalled = Get-ChildItem -Path $pkgDir -Directory -Filter 'Microsoft.Windows.WDK.x64.*' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $wdkInstalled) {
    Write-Host '==> Restauration du WDK (NuGet, ~500 Mo une seule fois)...' -ForegroundColor Cyan
    $nuget = Join-Path $env:TEMP 'nuget.exe'
    if (-not (Test-Path $nuget)) {
        Invoke-WebRequest -Uri 'https://dist.nuget.org/win-x86-commandline/latest/nuget.exe' -OutFile $nuget
    }
    & $nuget restore (Join-Path $root 'packages.config') -PackagesDirectory $pkgDir
    if ($LASTEXITCODE -ne 0) { throw 'nuget restore a echoue.' }
    # ré-inspection : le paquet vient d'être extrait, la variable de tout à
    # l'heure pointait encore sur rien (sinon la ceinture ci-dessous est muette)
    $wdkInstalled = Get-ChildItem -Path $pkgDir -Directory -Filter 'Microsoft.Windows.WDK.x64.*' |
        Select-Object -First 1
}

# 1) Locate msbuild — IMPÉRATIVEMENT 64 bits : les tâches WDK (InfVerif/
#    DPVerifierTask, ApiValidator) chargent leurs DLL natives DANS le procédé
#    et choisissent le sous-dossier par la bitness — un MSBuild 32 bits cherche
#    « x86\InfVerif.dll » / « x86\ApiValidator.exe » qui n'existent plus dans le
#    WDK 28000 (seulement x64/arm64) → DllNotFoundException / exit 1.
function Test-IsX64Pe([string]$exe) {
    if (-not (Test-Path $exe)) { return $false }
    $b = [IO.File]::ReadAllBytes($exe)
    $peOff = [BitConverter]::ToInt32($b, 0x3C)
    return ([BitConverter]::ToUInt16($b, $peOff + 4) -eq 0x8664)
}
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$candidates = @()
$fromPath = (Get-Command msbuild -ErrorAction SilentlyContinue | Select-Object -First 1).Source
if ($fromPath) { $candidates += $fromPath }
if (Test-Path $vswhere) {
    $candidates += & $vswhere -latest -products * -requires Microsoft.Component.MSBuild `
        -find MSBuild\**\Bin\amd64\MSBuild.exe
    $candidates += & $vswhere -latest -products * -requires Microsoft.Component.MSBuild `
        -find MSBuild\**\Bin\MSBuild.exe
}
$msbuild = $candidates | Where-Object { $_ -and (Test-IsX64Pe $_) } | Select-Object -First 1
if (-not $msbuild) {
    # dernier recours : le premier trouvé (avertira si 32 bits)
    $msbuild = $candidates | Where-Object { $_ } | Select-Object -First 1
}
if (-not $msbuild) { throw 'MSBuild introuvable - installez Visual Studio 2022+ avec la charge "Developpement C++".' }
$bitness = if (Test-IsX64Pe $msbuild) { '64 bits OK' } else { 'ATTENTION 32 bits - taches WDK natives en echec probable' }
Write-Host "    MSBuild : $msbuild ($bitness)"

# 2) Build (x64).
# _NT_TARGET_VERSION + TargetVersion : indispensables avec le WDK NuGet (cf.
# CI officielle de microsoft/Windows-driver-samples) — sans _NT_TARGET_VERSION,
# WDKBuildFolder reste vide et stampinf.exe est introuvable (TRK0005).
# 0xA000010 = Windows 10.0.26100 (bibliothèques Win11 24H2, compat Win10/11).
#
# Ceinture de sécurité (TRK0005) : le WDK NuGet n'enregistre PAS les macros
# WDKBuildFolder/InfToolPath dans Visual Studio ; si l'extension WDK installée
# sur la machine est d'une autre version que le paquet, la cible StampInf
# retombe sur un chemin de tâches vide ou x86 (stampinf.exe n'y vit plus en
# 28000+) et tracker échoue. On force donc, depuis le paquet réellement
# restauré : le dossier de build WDK, les répertoires x64 (stampinf) et x86
# (inf2cat), et la désactivation du tracker (inutile en CI fraîche).
$wdkProps = @('/p:TrackFileAccess=false')
if ($wdkInstalled) {
    $wdkBuildDir = Get-ChildItem -Path (Join-Path $wdkInstalled.FullName 'c\build') -Directory |
        Sort-Object Name -Descending | Select-Object -First 1
    if ($wdkBuildDir) {
        $wdkProps += "/p:WDKBuildFolder=$($wdkBuildDir.Name)"
        $stampinfDir = Join-Path $wdkInstalled.FullName "c\bin\$($wdkBuildDir.Name)\x64"
        $inf2catDir  = Join-Path $wdkInstalled.FullName "c\bin\$($wdkBuildDir.Name)\x86"
        if (Test-Path (Join-Path $stampinfDir 'stampinf.exe')) {
            $wdkProps += "/p:InfToolPath=$stampinfDir"
        }
        if (Test-Path (Join-Path $inf2catDir 'Inf2Cat.exe')) {
            $wdkProps += "/p:Inf2CatToolPath=$inf2catDir"
        }
        Write-Host "    WDK forcé : dossier $($wdkBuildDir.Name), stampinf=$stampinfDir"
    }
}
& $msbuild $sln /t:Build /p:"Configuration=$Configuration" /p:Platform=x64 `
    /p:TargetVersion=Windows10 /p:_NT_TARGET_VERSION=0xA000010 @wdkProps /m /v:m
if ($LASTEXITCODE -ne 0) { throw "Echec du build (code $LASTEXITCODE)." }

# 3) Collect outputs.
$objDir = Join-Path $root "roomeqvad\obj\x64\$Configuration"
New-Item -ItemType Directory -Force -Path $out | Out-Null
$files = @('roomeqvad.sys', 'roomeqvad.inf')
foreach ($f in $files) {
    $src = Get-ChildItem -Recurse -Path $objDir, (Join-Path $root 'roomeqvad') -Filter $f -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $src) { throw "$f introuvable apres build." }
    Copy-Item $src.FullName (Join-Path $out $f) -Force
    Write-Host "    $f <- $($src.FullName)"
}

# 4) inf2cat -> roomeqvad.cat
# Priorité au Inf2Cat DU PAQUET WDK NuGet (le dossier système Windows Kits\bin
# n'existe pas toujours sur les runners VS2026 — l'outillage WDK n'y vit plus).
$inf2cat = $null
if ($wdkInstalled -and $wdkBuildDir) {
    $pkgInf2Cat = Join-Path $wdkInstalled.FullName "c\bin\$($wdkBuildDir.Name)\x86\Inf2Cat.exe"
    if (Test-Path $pkgInf2Cat) { $inf2cat = $pkgInf2Cat }
}
if (-not $inf2cat) {
    $wdk = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending | Select-Object -First 1
    if ($wdk) {
        $inf2cat = Join-Path $wdk.FullName 'x64\inf2cat.exe'
        if (-not (Test-Path $inf2cat)) { $inf2cat = Join-Path $wdk.FullName 'x86\inf2cat.exe' }
    }
}
if ($inf2cat) {
    & $inf2cat /driver:$out /os:10_x64 | Out-Null
    Write-Host "    roomeqvad.cat genere (inf2cat : $inf2cat)."
} else {
    Write-Warning 'inf2cat.exe introuvable - le .cat sera genere par la CI.'
}

Write-Host "==> OK : sorties pilote dans $out" -ForegroundColor Green

if ($TestSign) {
    # signtool : fourni par le paquet SDK.CPP NuGet (bin/<ver>/x64) — le
    # dossier système Windows Kits\bin n'est pas fiable sur les runners VS2026.
    $sdkBin = ''
    $sdkPkg = Get-ChildItem -Path $pkgDir -Directory -Filter 'Microsoft.Windows.SDK.CPP.*' -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($sdkPkg -and $wdkBuildDir) {
        $cand = Join-Path $sdkPkg.FullName "c\bin\$($wdkBuildDir.Name)\x64"
        if (Test-Path (Join-Path $cand 'signtool.exe')) { $sdkBin = $cand }
    }
    & (Join-Path $PSScriptRoot 'sign_driver_test.ps1') -DriverDir $out -SdkBin $sdkBin
}
