<#
.SYNOPSIS
    Deploys the SLIMS portal on the public web server.

.DESCRIPTION
    Copies the build into place, registers it to start with Windows, starts it and waits until it
    answers. The previous copy is kept, so -Rollback puts it back.

    The portal is a plain Node process. It is registered as a scheduled task that runs at startup
    rather than a Windows service, because node.exe cannot talk to the service controller and this
    needs nothing installed beyond Node itself.

    IIS or Nginx sits in front and terminates HTTPS. See web.config and nginx-slims.conf in this
    folder.

.PARAMETER Source
    The build to deploy - the "run" folder from the release package. Defaults to ..\run.

.PARAMETER InstallDir
    Where the portal lives on this server. Defaults to C:\SLIMS\portal.

.PARAMETER Port
    The port the Node process listens on. IIS forwards to it. Defaults to 3001.

.PARAMETER BindAddress
    127.0.0.1 keeps the portal reachable only through the reverse proxy, which is what you want in
    front of a public name. Use 0.0.0.0 only if nothing is proxying.

.PARAMETER PublicUrl
    The address people open, printed at the end as the thing to check.

.EXAMPLE
    .\Deploy-Portal.ps1
    Deploy ..\run to C:\SLIMS\portal and start it.

.EXAMPLE
    .\Deploy-Portal.ps1 -DryRun
    Print every step without changing anything.

.EXAMPLE
    .\Deploy-Portal.ps1 -Rollback
    Restore the previous build and restart.
#>
[CmdletBinding()]
param(
    [string] $Source       = (Join-Path $PSScriptRoot "..\run"),
    [string] $InstallDir   = "C:\SLIMS\portal",
    [int]    $Port         = 3001,
    [string] $BindAddress  = "127.0.0.1",
    [string] $TaskName     = "SLIMS Portal",
    [string] $PublicUrl    = "https://streetlight.pera.gop.pk",
    [int]    $StartTimeoutSeconds = 90,
    [int]    $KeepBackups  = 5,
    [switch] $Rollback,
    [switch] $DryRun
)

$ErrorActionPreference = "Stop"
$BackupRoot = Join-Path (Split-Path $InstallDir -Parent) "portal-backups"
$StartScript = Join-Path $InstallDir "start-portal.cmd"

function Say    ($m) { Write-Host "  $m" }
function Step   ($m) { Write-Host ""; Write-Host "== $m" -ForegroundColor Cyan }
function Warn   ($m) { Write-Host "  ! $m" -ForegroundColor Yellow }
function Fail   ($m) { Write-Host ""; Write-Host "FAILED: $m" -ForegroundColor Red; exit 1 }
function Would  ($m) { if ($DryRun) { Write-Host "  [dry run] $m" -ForegroundColor DarkGray; return $true } return $false }

# ---------------------------------------------------------------- checks ----
function Assert-Administrator {
    # A dry run changes nothing, so it does not need elevation - handy for checking a build first.
    if ($DryRun) { return }
    $me = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $me.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Fail "Run this from an elevated PowerShell (Run as administrator)."
    }
}

function Assert-Node {
    $node = Get-Command node -ErrorAction SilentlyContinue
    if ($null -eq $node) { Fail "Node.js is not on PATH. Install the current LTS from nodejs.org, then reopen PowerShell." }
    $raw = (& node --version) 2>$null          # v20.11.1
    $major = 0
    if ($raw -match '^v(\d+)\.') { $major = [int]$Matches[1] }
    if ($major -lt 20) { Fail "Node.js $raw is too old. The portal needs 20 or newer." }
    Say "Node.js $raw at $($node.Source)"
}

function Assert-Build ($path) {
    if (-not (Test-Path $path)) { Fail "No build at $path" }
    if (-not (Test-Path (Join-Path $path "server.js"))) {
        Fail "$path does not look like a portal build - server.js is missing. Point -Source at the 'run' folder from the release package."
    }
    if (-not (Test-Path (Join-Path $path ".next\static"))) {
        Warn "There is no .next\static in the build. Pages will load but every stylesheet and script will 404."
    }
    $manifest = Join-Path $path ".next\routes-manifest.json"
    if (Test-Path $manifest) {
        $api = (Get-Content $manifest -Raw | Select-String -Pattern 'https?://[^"]+/api/' -AllMatches).Matches |
               ForEach-Object { $_.Value } | Select-Object -First 1
        if ($api) { Say "This build calls the API at $api" }
    }
}

# ---------------------------------------------------------------- process ----
function Stop-Portal {
    $stopped = $false
    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if ($null -ne $task) {
        if (-not (Would "stop scheduled task '$TaskName'")) {
            Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
            $stopped = $true
        }
    }
    $listeners = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
    foreach ($l in $listeners) {
        if (-not (Would "stop the process holding port $Port (pid $($l.OwningProcess))")) {
            Stop-Process -Id $l.OwningProcess -Force -ErrorAction SilentlyContinue
            $stopped = $true
        }
    }
    if ($stopped) { Start-Sleep -Seconds 3; Say "Portal stopped." } else { Say "Portal was not running." }
}

function Write-StartScript {
    $text = @"
@echo off
REM Written by Deploy-Portal.ps1 - edit the values here if the port or address changes.
set NODE_ENV=production
set HOSTNAME=$BindAddress
set PORT=$Port
cd /d "%~dp0"
node server.js
"@
    if (Would "write $StartScript") { return }
    Set-Content -Path $StartScript -Value $text -Encoding ASCII
}

function Register-PortalTask {
    if (Would "register scheduled task '$TaskName' to run at startup") { return }
    $action  = New-ScheduledTaskAction -Execute "cmd.exe" -Argument "/c `"$StartScript`"" -WorkingDirectory $InstallDir
    $trigger = New-ScheduledTaskTrigger -AtStartup
    $set     = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
                                            -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
    $auth    = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $set -Principal $auth -Force | Out-Null
    Say "Scheduled task '$TaskName' registered (starts with Windows, restarts up to 3 times)."
}

function Start-Portal {
    if (Would "start scheduled task '$TaskName'") { return }
    Start-ScheduledTask -TaskName $TaskName
}

function Wait-ForPortal {
    $url = "http://127.0.0.1:$Port/login"
    for ($i = 0; $i -lt $StartTimeoutSeconds; $i++) {
        Start-Sleep -Seconds 1
        try {
            $r = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 5
            if ($r.StatusCode -eq 200) { return $true }
        } catch { }
    }
    return $false
}

# ---------------------------------------------------------------- files ------
function Backup-Current {
    if (-not (Test-Path $InstallDir)) { Say "Nothing to back up - first deployment."; return $null }
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $dest = Join-Path $BackupRoot $stamp
    if (Would "back up $InstallDir to $dest") { return $dest }
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    robocopy $InstallDir $dest /E /NFL /NDL /NJH /NJS /NP | Out-Null
    Say "Previous build kept at $dest"
    $old = Get-ChildItem $BackupRoot -Directory | Sort-Object Name -Descending | Select-Object -Skip $KeepBackups
    foreach ($o in $old) { Remove-Item $o.FullName -Recurse -Force -ErrorAction SilentlyContinue }
    return $dest
}

function Copy-Build ($from) {
    if (Would "mirror $from into $InstallDir") { return }
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    # /MIR so a file dropped from the build is dropped from the server too. start-portal.cmd is
    # rewritten afterwards, so excluding it here would serve no purpose.
    robocopy $from $InstallDir /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { Fail "robocopy failed with exit code $LASTEXITCODE" }
    Say "Build copied."
}

function Restore-Latest {
    if (-not (Test-Path $BackupRoot)) { Fail "There are no backups under $BackupRoot" }
    $latest = Get-ChildItem $BackupRoot -Directory | Sort-Object Name -Descending | Select-Object -First 1
    if ($null -eq $latest) { Fail "There are no backups under $BackupRoot" }
    Say "Restoring $($latest.FullName)"
    Copy-Build $latest.FullName
}

# ---------------------------------------------------------------- run --------
Write-Host ""
Write-Host "SLIMS portal deployment" -ForegroundColor White
Write-Host "  install  $InstallDir"
Write-Host "  listens  http://$BindAddress`:$Port"
if ($DryRun) { Write-Host "  DRY RUN - nothing will be changed" -ForegroundColor DarkGray }

Assert-Administrator
Assert-Node

if ($Rollback) {
    Step "Rolling back"
    Stop-Portal
    Restore-Latest
    Write-StartScript
} else {
    $Source = (Resolve-Path $Source -ErrorAction SilentlyContinue).Path
    if (-not $Source) { Fail "-Source does not exist" }
    Step "Checking the build"
    Assert-Build $Source
    Step "Stopping the portal"
    Stop-Portal
    Step "Backing up the current build"
    Backup-Current | Out-Null
    Step "Installing the new build"
    Copy-Build $Source
    Write-StartScript
    Step "Registering the startup task"
    Register-PortalTask
}

Step "Starting the portal"
Start-Portal
if ($DryRun) {
    Write-Host ""
    Write-Host "Dry run finished. Nothing was changed. Run it again without -DryRun to deploy." -ForegroundColor DarkGray
    Write-Host ""
    exit 0
}
if (Wait-ForPortal) {
    Say "The portal answered on http://127.0.0.1:$Port"
    Write-Host ""
    Write-Host "Done. Check $PublicUrl in a browser." -ForegroundColor Green
    Write-Host "If that does not load, the reverse proxy is the next place to look - see web.config."
} else {
    Warn "The portal did not answer within $StartTimeoutSeconds seconds."
    Warn "Run $StartScript by hand in a console window to see what it prints."
    exit 1
}
Write-Host ""
