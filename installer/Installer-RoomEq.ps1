# ============================================================================
#  RoomEq — Installation assistée (débloque + installe + journal complet)
#  ----------------------------------------------------------------------------
#  Usage (invite administrateur, depuis le dossier contenant le MSI) :
#    powershell -ExecutionPolicy Bypass -File .\Installer-RoomEq.ps1
#
#  Le script :
#    1. se relance élevé (UAC) si la session n'est pas administrateur ;
#    2. retrouve le MSI RoomEq (même dossier, sinon Téléchargements) ;
#    3. retire la marque « du web » (Unblock-File) qui déclenche SmartScreen ;
#    4. lance msiexec avec journal détaillé (%TEMP%\RoomEq-install.log) ;
#    5. affiche le résultat et, en cas d'échec, ouvre le journal à la
#       première ligne « RoomEq CA ERREUR ».
# ============================================================================

$ErrorActionPreference = 'Stop'
$MsiName = 'RoomEq-2.0.0-win-x64.msi'
$LogFile = Join-Path $env:TEMP 'RoomEq-install.log'

# --- 1) auto-élévation -------------------------------------------------------
$isAdmin = ([Security.Principal.WindowsPrincipal] `
    [Security.Principal.WindowsIdentity]::GetCurrent()
  ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "Relance en administrateur (acceptez l'invite UAC)..." -ForegroundColor Yellow
    $arg = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    Start-Process powershell.exe -Verb RunAs -ArgumentList $arg
    exit
}

Write-Host "== RoomEq — installation assistee ==" -ForegroundColor Cyan

# --- 2) retrouver le MSI -----------------------------------------------------
$scriptDir  = Split-Path -Parent $PSCommandPath
$downloads  = Join-Path $env:USERPROFILE 'Downloads'
$msi = @((Join-Path $scriptDir $MsiName), (Join-Path $downloads $MsiName)) |
       Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $msi) {
    Write-Host "MSI introuvable : placez $MsiName dans le dossier du script" `
        -ForegroundColor Red
    Write-Host "  ou dans : $downloads"
    Read-Host 'Appuyez sur Entree pour fermer'
    exit 1
}
Write-Host "MSI : $msi"

# --- 3) retirer la marque du web (SmartScreen) -------------------------------
try {
    Unblock-File -Path $msi -ErrorAction Stop
    Write-Host "Marque Internet retiree (SmartScreen ne bloquera plus ce fichier)." `
        -ForegroundColor Green
} catch {
    Write-Host "Deblocage sans effet (fichier deja local) : poursuite." -ForegroundColor DarkGray
}

# --- 4) installation avec journal --------------------------------------------
if (Test-Path $LogFile) { Remove-Item $LogFile -Force }
Write-Host "Installation en cours (le journal est ecrit dans :" -ForegroundColor Cyan
Write-Host "  $LogFile )" -ForegroundColor Cyan

$proc = Start-Process msiexec.exe `
    -ArgumentList "/i `"$msi`" /l*v `"$LogFile`"" `
    -Wait -PassThru
$code = $proc.ExitCode

# --- 5) interpretation du code ----------------------------------------------
switch ($code) {
    0     { Write-Host "SUCCES : RoomEq est installe (cartes RoomEq Input/Output" `
                 -ForegroundColor Green
            Write-Host "        presentes ; redemarrez si l'installateur le propose)." `
                 -ForegroundColor Green }
    3010  { Write-Host "SUCCES : installation terminee — REDEMARRAGE REQUIS" `
                 -ForegroundColor Green
            Write-Host "        (activation du mode test de Windows)." -ForegroundColor Green }
    1603  { Write-Host "ECHEC 1603 : voir le journal (ligne 'RoomEq CA ERREUR')." `
                 -ForegroundColor Red
            Write-Host "        Causes frequentes : Secure Boot actif (cas 5 du guide" `
                 -ForegroundColor Red
            Write-Host "        TROUBLESHOOT-INSTALLATION.md) ou antivirus (cas 3)." `
                 -ForegroundColor Red }
    1618  { Write-Host "ECHEC 1618 : une autre installation est deja en cours." `
                 -ForegroundColor Red
            Write-Host "        Fermez-la puis relancez ce script." -ForegroundColor Red }
    1625  { Write-Host "ECHEC 1625 : installation interdite par la strategie systeme." `
                 -ForegroundColor Red
            Write-Host "        PC gere par une organisation ? (cas 6 du guide)." -ForegroundColor Red }
    1730  { Write-Host "ECHEC 1730 : paquet non approuve par la strategie." `
                 -ForegroundColor Red
            Write-Host "        Antivirus ou PC gere (cas 3 / cas 6 du guide)." -ForegroundColor Red }
    default { Write-Host "Code de sortie : $code (voir le journal)." -ForegroundColor Yellow }
}

# échec -> ouvrir le journal sur la première erreur RoomEq
if ($code -ne 0 -and $code -ne 3010 -and (Test-Path $LogFile)) {
    $hit = Select-String -Path $LogFile -Pattern 'RoomEq CA ERREUR' |
           Select-Object -First 1
    if ($hit) {
        Write-Host ""
        Write-Host "Premiere erreur trouvee dans le journal :" -ForegroundColor Yellow
        Write-Host ("  " + $hit.Line) -ForegroundColor White
        Write-Host "  (ligne " + $hit.LineNumber + " de $LogFile)" -ForegroundColor DarkGray
    }
    Start-Process notepad.exe $LogFile
}

Read-Host 'Appuyez sur Entree pour fermer'
