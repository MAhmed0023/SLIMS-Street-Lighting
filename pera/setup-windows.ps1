# =====================================================================================================
#  PERA Workflow - one-time setup of a Windows development / evaluation PC.
#
#  Installs (with winget, skipping anything already present):
#     Git, .NET 9 SDK, Node.js LTS, and - with -WithSqlServer - SQL Server 2022 Express
#  Then clones (or updates) the code, restores the .NET packages, installs the web packages,
#  and writes START-PERA-DEV.bat next to the code.
#
#  Run from PowerShell:
#     powershell -ExecutionPolicy Bypass -File setup-windows.ps1
#     powershell -ExecutionPolicy Bypass -File setup-windows.ps1 -WithSqlServer
#  Windows may ask for administrator permission while installing; allow it.
# =====================================================================================================
param(
    [string]$Target = "C:\Users\My Working Codes\PERA taskmanagement",
    [string]$Repo = "https://github.com/MAhmed0023/SLIMS-Street-Lighting.git",
    [string]$Branch = "claude/great-allen-77wf32",
    [switch]$WithSqlServer
)
$ErrorActionPreference = "Stop"

function Step($m) { Write-Host ""; Write-Host "== $m" -ForegroundColor Cyan }
function RefreshPath {
    $env:Path = [Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [Environment]::GetEnvironmentVariable("Path", "User")
}
function Have($cmd) { [bool](Get-Command $cmd -ErrorAction SilentlyContinue) }
function Install($id, $name) {
    Write-Host "   Installing $name ($id) ..."
    winget install --id $id -e --silent --accept-package-agreements --accept-source-agreements
    # -1978335189 / -1978335135: winget reports the package is already installed or up to date
    if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne -1978335189 -and $LASTEXITCODE -ne -1978335135) {
        throw "winget could not install $name (exit code $LASTEXITCODE)."
    }
    RefreshPath
}

Step "Checking winget"
if (-not (Have winget)) {
    throw "winget is not available. Install 'App Installer' from the Microsoft Store, then run this script again."
}

Step "Git"
if (Have git) { Write-Host "   already installed: $(git --version)" } else { Install "Git.Git" "Git" }

Step ".NET 9 SDK"
$sdks = if (Have dotnet) { (dotnet --list-sdks) -join "`n" } else { "" }
if ($sdks -match "(?m)^9\.") { Write-Host "   already installed" } else { Install "Microsoft.DotNet.SDK.9" ".NET 9 SDK" }

Step "Node.js"
$nodeOk = $false
if (Have node) { $v = (node -v).TrimStart("v").Split(".")[0]; $nodeOk = [int]$v -ge 20 }
if ($nodeOk) { Write-Host "   already installed: $(node -v)" } else { Install "OpenJS.NodeJS.LTS" "Node.js LTS" }

if ($WithSqlServer) {
    Step "SQL Server 2022 Express"
    if (Get-Service -Name 'MSSQL$SQLEXPRESS' -ErrorAction SilentlyContinue) { Write-Host "   already installed (instance SQLEXPRESS)" }
    else { Install "Microsoft.SQLServer.2022.Express" "SQL Server 2022 Express" }
}

RefreshPath
foreach ($c in "git", "dotnet", "node", "npm") { if (-not (Have $c)) { throw "$c is still not on the PATH. Close this window, open a new PowerShell and run the script again." } }

Step "Code: $Target"
if (Test-Path (Join-Path $Target ".git")) {
    git -C $Target fetch origin $Branch
    git -C $Target checkout $Branch
    git -C $Target pull origin $Branch
} else {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Target) | Out-Null
    git clone -b $Branch $Repo $Target
}
if ($LASTEXITCODE -ne 0) { throw "git could not get the code. If the repository is private, sign in to GitHub when asked." }

Step "Restoring .NET packages"
dotnet restore (Join-Path $Target "pera\backend\Pera.sln")
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed." }

Step "Installing web packages"
Push-Location (Join-Path $Target "pera\frontend")
npm ci --no-audit --no-fund
$npmExit = $LASTEXITCODE
Pop-Location
if ($npmExit -ne 0) { throw "npm install failed." }

Step "Writing START-PERA-DEV.bat"
$useSql = [bool](Get-Service -Name 'MSSQL$SQLEXPRESS' -ErrorAction SilentlyContinue)
$dbLines = "set Database__Provider=Sqlite`r`nset ConnectionStrings__Default=Data Source=%~dp0pera-demo.db"
if ($useSql) { $dbLines = "REM SQL Server Express found: the API uses the database 'Pera' on .\SQLEXPRESS" }
$bat = @"
@echo off
REM Starts PERA from the source code: API on http://localhost:5092, web app on http://localhost:3002
cd /d "%~dp0"
$dbLines
start "PERA API" /d "%~dp0pera\backend\src\Pera.Api" cmd /k dotnet run
start "PERA Web" /d "%~dp0pera\frontend" cmd /k npm run dev
echo Waiting for the web app ...
timeout /t 20 /nobreak >nul
start "" http://localhost:3002
"@
Set-Content -Path (Join-Path $Target "START-PERA-DEV.bat") -Value $bat -Encoding ASCII

Write-Host ""
Write-Host "Setup complete." -ForegroundColor Green
Write-Host "   Start PERA:  double-click  $Target\START-PERA-DEV.bat"
Write-Host "   Then open    http://localhost:3002   and sign in as  sysadmin"
Write-Host "                (the password you type the first time becomes the administrator password)"
if (-not $useSql) { Write-Host "   Database:    demonstration file (pera-demo.db). For real records run this script with -WithSqlServer." }
