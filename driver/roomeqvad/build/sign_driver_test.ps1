<#
.SYNOPSIS
    Test-signe le pilote RoomEq (roomeqvad.sys/.cat) avec un certificat local.

.DESCRIPTION
    Voie GRATUITE pour tester le pilote : cree (une fois) un certificat
    auto-signe "CN=RoomEq Test Signing", signe le .sys et le .cat, exporte
    le certificat public, et affiche la procedure a suivre :

      1. [admin] certutil -addstore Root RoomEq-TestSigning.cer
         certutil -addstore TrustedPublisher RoomEq-TestSigning.cer
      2. [admin] bcdedit /set testsigning on
      3. Redemarrer (un filigrane "Mode de test" apparait en bas a droite).
      4. Installer : pnputil /add-driver roomeqvad.inf /install /scan

    Pour retirer le mode test : bcdedit /deletevalue testsigning + redemarrer.
    Pour une distribution PROPRE au grand public, voir INSTALLATION-PILOTE.md
    (attestation Microsoft - certificat EV + compte Hardware Dev Center).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File sign_driver_test.ps1 -DriverDir .\build\x64
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$DriverDir,
    [string]$CertSubject = 'CN=RoomEq Test Signing',
    # répertoire bin/x64 du paquet SDK.CPP NuGet (signtool.exe y vit) —
    # transmis par build_driver.ps1 ; vide → repli système Windows Kits
    [string]$SdkBin = ''
)

$ErrorActionPreference = 'Stop'

# 1) signtool : paquet SDK NuGet > Windows Kits système
$signtool = ''
if ($SdkBin -and (Test-Path (Join-Path $SdkBin 'signtool.exe'))) {
    $signtool = Join-Path $SdkBin 'signtool.exe'
} else {
    $kit = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending | Select-Object -First 1
    if ($kit) {
        $signtool = Join-Path $kit.FullName 'x64\signtool.exe'
        if (-not (Test-Path $signtool)) { $signtool = Join-Path $kit.FullName 'x86\signtool.exe' }
    }
}
if (-not $signtool -or -not (Test-Path $signtool)) { throw 'signtool.exe introuvable (paquet SDK NuGet ni Windows Kits systeme).' }

# 2) certificat local (cree une seule fois dans CurrentUser\My)
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $CertSubject } |
    Select-Object -First 1
if (-not $cert) {
    Write-Host "==> Creation du certificat de test : $CertSubject" -ForegroundColor Cyan
    $cert = New-SelfSignedCertificate -Subject $CertSubject -Type CodeSigningCert `
        -KeyUsage DigitalSignature -FriendlyName 'RoomEq Test Signing' `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -NotAfter (Get-Date).AddYears(5)
}

# 3) export du .cer (pour l'installation dans les magasins de la cible)
$cerPath = Join-Path $DriverDir 'RoomEq-TestSigning.cer'
[IO.File]::WriteAllBytes($cerPath, $cert.Export('Cert'))
Write-Host "    certificat public : $cerPath"

# 4) signature du .sys et du .cat
foreach ($f in @('roomeqvad.sys', 'roomeqvad.cat')) {
    $p = Join-Path $DriverDir $f
    if (Test-Path $p) {
        & $signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 `
            /sha1 $cert.Thumbprint /v $p
        if ($LASTEXITCODE -ne 0) { throw "Signature de $f echouee." }
    }
}

Write-Host '==> Pilote signe-test.' -ForegroundColor Green
Write-Host @'

PROCEDURE D'INSTALLATION (ordinateur cible, session administrateur) :
  1. certutil -addstore Root RoomEq-TestSigning.cer
     certutil -addstore TrustedPublisher RoomEq-TestSigning.cer
  2. bcdedit /set testsigning on
  3. REDEMARRER (filigrane « Mode de test » en bas a droite = normal)
  4. pnputil /add-driver roomeqvad.inf /install /scan
     -> les peripheriques « RoomEq Input / RoomEq Output (RoomEq Virtual
        Audio) » apparaissent dans le panneau son.

Retirer le mode test :  bcdedit /deletevalue testsigning  puis redemarrer.

'@ -ForegroundColor Yellow
