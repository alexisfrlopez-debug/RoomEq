# ============================================================================
#  RoomEq -- Installation assistee v2 (diagnostic + reparation + installation)
#  ----------------------------------------------------------------------------
#  Usage : clic droit sur ce fichier -> "Executer avec PowerShell"
#  (ou, dans une invite : powershell -ExecutionPolicy Bypass -File .\Installer-RoomEq.ps1)
#
#  Nouveautes v2 (apres echec de la commande Add-MpPreference seule) :
#    1. se relance eleve (UAC) si la session n'est pas administrateur ;
#    2. DIAGNOSTIQUE la machine AVANT d'installer : anti-ransomware (acces
#       controle aux dossiers), Secure Boot, mode testsigning, pilote dans
#       le magasin, peripherique, application deja installee ;
#    3. REPARATION automatique : coupe l'acces controle aux dossiers le
#       temps de l'installation (la whitelist msiexec seule ne suffit pas :
#       SrTasks/bcdedit/pnputil sont aussi bloques) puis le REACTIVE a la fin ;
#    4. debloque le MSI (marque du web / SmartScreen) et installe avec
#       journal detaille (%TEMP%\RoomEq-install.log) ;
#    5. produit un RAPPORT final a copier-coller tel quel en cas de probleme
#       (il contient le verdict : Secure Boot, antivirus, ou autre).
# ============================================================================

$ErrorActionPreference = 'Continue'
$MsiName = 'RoomEq-2.0.0-win-x64.msi'
$LogFile = Join-Path $env:TEMP 'RoomEq-install.log'
$Rapport = New-Object System.Collections.Generic.List[string]

function Add-Rap([string]$cle, [string]$valeur) {
    $script:Rapport.Add(("{0,-22}: {1}" -f $cle, $valeur))
}

# --- 1) auto-elevation --------------------------------------------------------
$isAdmin = ([Security.Principal.WindowsPrincipal] `
    [Security.Principal.WindowsIdentity]::GetCurrent()
  ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "Relance en administrateur (acceptez l'invite UAC)..." -ForegroundColor Yellow
    $arg = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    Start-Process powershell.exe -Verb RunAs -ArgumentList $arg
    exit
}

Write-Host "== RoomEq -- installation assistee v2 ==" -ForegroundColor Cyan
Write-Host ""

# --- 2) diagnostic prealable ---------------------------------------------------
Write-Host "--- DIAGNOSTIC AVANT INSTALLATION ----------------------------" -ForegroundColor Cyan

# Windows
try {
    $os = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop
    $winTxt = "$($os.Caption) (build $($os.BuildNumber))"
} catch { $winTxt = "indetermine" }
Add-Rap 'Windows' $winTxt
Write-Host "Windows                : $winTxt"

# Defender actif ?
$defenderActif = $false
try { $defenderActif = ((Get-Service WinDefend -ErrorAction Stop).Status -eq 'Running') } catch {}
$defTxt = $(if ($defenderActif) { 'Actif' } else { 'Arrete ou absent (antivirus tiers ?)' })
Add-Rap 'Service Defender' $defTxt
Write-Host "Service Defender       : $defTxt"

# Acces controle aux dossiers (CFA / anti-ransomware)
$cfaEtat = 'Indetermine'
$cfaAutorises = @()
if ($defenderActif) {
    try {
        $p = Get-MpPreference -ErrorAction Stop
        $cfaAutorises = @($p.ControlledFolderAccessAllowedApplications)
        switch ($p.EnableControlledFolderAccess) {
            0   { $cfaEtat = 'Desactive' }
            1   { $cfaEtat = 'ACTIVE' }
            2   { $cfaEtat = 'Audit (journalise seulement)' }
        }
    } catch { $cfaEtat = 'Indetermine' }
}
$msiAutorise = ($cfaAutorises -contains 'C:\Windows\System32\msiexec.exe')
Add-Rap 'Anti-ransomware (CFA)' $cfaEtat
Add-Rap '  msiexec autorise' $(if ($msiAutorise) { 'Oui' } else { 'Non' })
Write-Host "Anti-ransomware (CFA)  : $cfaEtat (msiexec autorise: $(if ($msiAutorise) {'Oui'} else {'Non'}))"
if ($cfaEtat -eq 'ACTIVE' -and $msiAutorise) {
    Write-Host "  NB: autoriser msiexec SEUL ne suffit pas : SrTasks.exe, bcdedit.exe" -ForegroundColor DarkYellow
    Write-Host "  et pnputil.exe sont bloques pareillement -> le script coupera la" -ForegroundColor DarkYellow
    Write-Host "  protection pendant l'installation (et la retablira ensuite)." -ForegroundColor DarkYellow
}

# Secure Boot
$sb = 'Indetermine'
try { $sb = $(if (Confirm-SecureBootUEFI -ErrorAction Stop) { 'ACTIF' } else { 'Inactif' }) } catch {}
Add-Rap 'Secure Boot' $sb
Write-Host "Secure Boot            : $sb"
if ($sb -eq 'ACTIF') {
    Write-Host "  ATTENTION: Secure Boot ACTIF = le pilote signe-test ne pourra PAS" -ForegroundColor Yellow
    Write-Host "  charger (le mode testsigning sera refuse). L'installation peut" -ForegroundColor Yellow
    Write-Host "  echouer avec le message 'mode de signature de test' -> cas 5 du guide." -ForegroundColor Yellow
}

# Mode testsigning
$ts = 'Indetermine'
try {
    $bcd = bcdedit /enum "{current}" 2>$null
    $l = $bcd | Select-String -Pattern 'testsigning' | Select-Object -First 1
    if ($l) {
        if ($l.Line -match '(Oui|Yes)\s*$') { $ts = 'Actif' } else { $ts = 'Inactif' }
    }
} catch {}
Add-Rap 'Mode testsigning' $ts
Write-Host "Mode testsigning       : $ts"

# Pilote deja present dans le magasin ?
$pilote = 'Absent'
try {
    $d = pnputil /enum-drivers 2>$null | Select-String 'roomeqvad.inf'
    if ($d) { $pilote = 'Present' }
} catch {}
Add-Rap 'Pilote (magasin)' $pilote

# Peripherique deja cree ?
$periph = 'Absent'
try {
    $dev = Get-PnpDevice -ErrorAction Stop | Where-Object { $_.InstanceId -like 'ROOT\ROOMEQVAD*' }
    if ($dev) { $periph = "Present (statut $($dev.Status))" }
} catch {}
Add-Rap 'Peripherique' $periph

# Application deja installee ?
$appEtat = 'Non installee'
try {
    $app = Get-ItemProperty `
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*', `
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*' `
        -ErrorAction Stop | Where-Object { $_.DisplayName -like 'RoomEq*' }
    if ($app) { $appEtat = "Installee ($($app.DisplayName) v$($app.DisplayVersion))" }
} catch {}
Add-Rap 'Application' $appEtat
Write-Host "Pilote / periph. / app  : $pilote / $periph / $appEtat"
Write-Host ""

# --- 3) reparation : couper l'anti-ransomware pendant l'installation -----------
$cfaACouper = $false
if ($cfaEtat -eq 'ACTIVE') {
    Write-Host "--- PROTECTION ANTI-RANSOMWARE -------------------------------" -ForegroundColor Cyan
    Write-Host "L'acces controle aux dossiers est ACTIVE : c'est lui qui bloque" -ForegroundColor Yellow
    Write-Host "l'installation (notifications 'Modifications non autorisees bloques')." -ForegroundColor Yellow
    Write-Host "Appuyez sur Entree pour le COUPER pendant l'installation seulement" -ForegroundColor Yellow
    Write-Host "(il sera REACTIVE automatiquement a la fin du script)."
    Write-Host "Ou repondez n pour tenter l'installation sans y toucher."
    $rep = Read-Host 'Couper la protection le temps de l installation ? (O/n)'
    if ($rep -eq '' -or $rep -match '^[oOyY]') {
        try {
            Set-MpPreference -EnableControlledFolderAccess Disabled -ErrorAction Stop
            $cfaACouper = $true
            Write-Host "Acces controle aux dossiers : coupe pour l'installation." -ForegroundColor Green
        } catch {
            Write-Host "Impossible de modifier le parametre : $($_.Exception.Message)" -ForegroundColor Red
            Write-Host "Coupez-le manuellement (Securite Windows > Protection contre les" -ForegroundColor Yellow
            Write-Host "ransomware > Gerer > Acces controle aux dossiers > Desactive) puis" -ForegroundColor Yellow
            Write-Host "relancez ce script." -ForegroundColor Yellow
            Read-Host 'Appuyez sur Entree pour fermer'
            exit 1
        }
    }
}
Write-Host ""

# --- 4) retrouver le MSI -------------------------------------------------------
$scriptDir = Split-Path -Parent $PSCommandPath
$downloads = Join-Path $env:USERPROFILE 'Downloads'
$msi = @((Join-Path $scriptDir $MsiName), (Join-Path $downloads $MsiName)) |
       Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $msi) {
    Write-Host "MSI introuvable : placez $MsiName dans le dossier du script" -ForegroundColor Red
    Write-Host "  ou dans : $downloads"
    Add-Rap 'Resultat' 'MSI INTROUVABLE'
    Read-Host 'Appuyez sur Entree pour fermer'
    exit 1
}
Add-Rap 'MSI' $msi
Write-Host "MSI : $msi"

# --- 5) retirer la marque du web (SmartScreen) ---------------------------------
try {
    Unblock-File -Path $msi -ErrorAction Stop
    Write-Host "Marque Internet retiree (SmartScreen ne bloquera plus ce fichier)." -ForegroundColor Green
} catch {
    Write-Host "Deblocage sans effet (fichier deja local) : poursuite." -ForegroundColor DarkGray
}

# --- 6) installation avec journal ----------------------------------------------
if (Test-Path $LogFile) { Remove-Item $LogFile -Force }
Write-Host ""
Write-Host "Installation en cours (patientez 1 a 3 minutes)..." -ForegroundColor Cyan
Write-Host "  Journal : $LogFile"

$proc = Start-Process msiexec.exe `
    -ArgumentList "/i `"$msi`" /l*v `"$LogFile`"" `
    -Wait -PassThru
$code = $proc.ExitCode
Add-Rap 'Code msiexec' $code
Write-Host ""
Write-Host "Code de sortie msiexec : $code" -ForegroundColor $(if ($code -eq 0 -or $code -eq 3010) { 'Green' } else { 'Red' })

# --- 7) restauration de la protection ------------------------------------------
if ($cfaACouper) {
    try {
        Set-MpPreference -EnableControlledFolderAccess Enabled -ErrorAction Stop
        Write-Host "Acces controle aux dossiers : REACTIVE (protection de retour)." -ForegroundColor Green
        Add-Rap 'CFA apres script' 'Reactivate'
    } catch {
        Write-Host "ATTENTION : reactivez manuellement l'acces controle aux dossiers" -ForegroundColor Red
        Write-Host "(Securite Windows > Protection contre les ransomware)." -ForegroundColor Red
        Add-Rap 'CFA apres script' 'A REACTIVER MANUELLEMENT'
    }
}

# --- 8) analyse du journal + verdict -------------------------------------------
$caLines = @()
if (Test-Path $LogFile) {
    $caLines = @(Select-String -Path $LogFile -Pattern 'RoomEq CA' |
                 ForEach-Object { $_.Line.Trim() })
}

$verdict = ''
if ($code -eq 0) {
    $verdict = 'INSTALLATION REUSSIE (lancez RoomEq depuis le menu Demarrer)'
} elseif ($code -eq 3010) {
    $verdict = 'INSTALLATION REUSSIE -- REDEMARRAGE REQUIS (activation du mode test)'
} elseif ($caLines | Where-Object { $_ -match 'signature de test|bcdedit' }) {
    $verdict = 'BLOCAGE : SECURE BOOT (cas 5 du guide) -- le mode test ne peut pas ' +
               "s'activer tant que Secure Boot est actif. Solutions : le desactiver " +
               'dans le BIOS/UEFI, ou attendre le pilote signe Microsoft (Phase A).'
} elseif ($caLines.Count -eq 0) {
    $verdict = 'ECHEC AVANT LES ACTIONS ROOMEQ : moteur d installation bloque ' +
               '(anti-ransomware/antivirus, cas 3/3bis du guide) -- code ' + $code
} else {
    $err = ($caLines | Where-Object { $_ -match 'ERREUR' } | Select-Object -First 1)
    $verdict = "ECHEC : $err"
}
Add-Rap 'Verdict' $verdict

# etat final reel (pilote/peripherique) apres installation
try {
    $d2 = pnputil /enum-drivers 2>$null | Select-String 'roomeqvad.inf'
    $dev2 = Get-PnpDevice -ErrorAction Stop | Where-Object { $_.InstanceId -like 'ROOT\ROOMEQVAD*' }
    Add-Rap 'Apres install' ("pilote: $(if ($d2) {'Present'} else {'Absent'}) / " +
                             "peripherique: $(if ($dev2) {"Present ($($dev2.Status))"} else {'Absent'})")
} catch {}

# --- 9) rapport final a copier-coller ------------------------------------------
Write-Host ""
Write-Host "==============================================================" -ForegroundColor Cyan
Write-Host " RAPPORT ROOMEQ -- copiez-collez ce bloc EN ENTIER si vous" -ForegroundColor Cyan
Write-Host " demandez de l'aide (il contient le diagnostic complet)" -ForegroundColor Cyan
Write-Host "==============================================================" -ForegroundColor Cyan
Write-Host "--- DEBUT RAPPORT ROOMEQ ---"
foreach ($l in $Rapport) { Write-Host "ROOMEQ | $l" }
if ($caLines.Count -gt 0) {
    foreach ($l in $caLines) { Write-Host "ROOMEQ-LOG | $l" }
} else {
    Write-Host "ROOMEQ-LOG | (aucune ligne RoomEq CA dans le journal)"
}
Write-Host "--- FIN RAPPORT ROOMEQ ---"
Write-Host "==============================================================" -ForegroundColor Cyan

# echec -> ouvrir le journal pour consultation
if ($code -ne 0 -and $code -ne 3010 -and (Test-Path $LogFile)) {
    Start-Process notepad.exe $LogFile
}

Read-Host 'Appuyez sur Entree pour fermer'
