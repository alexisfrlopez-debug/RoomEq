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
}

# 1) Locate msbuild (VS installation).
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) { throw 'vswhere.exe introuvable - installez Visual Studio 2022.' }
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild `
    -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1
if (-not $msbuild) { throw 'MSBuild introuvable - installez VS 2022 avec la charge "Developpement C++".' }

# 2) Build (x64).
& $msbuild $sln /t:Build /p:"Configuration=$Configuration" /p:Platform=x64 /m /v:m
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
$wdk = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Directory |
    Sort-Object Name -Descending | Select-Object -First 1
$inf2cat = Join-Path $wdk.FullName 'x64\inf2cat.exe'
if (-not (Test-Path $inf2cat)) { $inf2cat = Join-Path $wdk.FullName 'x86\inf2cat.exe' }
if (Test-Path $inf2cat) {
    & $inf2cat /driver:$out /os:10_x64 | Out-Null
    Write-Host "    roomeqvad.cat genere (inf2cat)."
} else {
    Write-Warning 'inf2cat.exe introuvable - le .cat sera genere par la CI.'
}

Write-Host "==> OK : sorties pilote dans $out" -ForegroundColor Green

if ($TestSign) {
    & (Join-Path $PSScriptRoot 'sign_driver_test.ps1') -DriverDir $out
}
