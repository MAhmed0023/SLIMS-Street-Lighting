# Writes api\appsettings.Production.json on first start: fresh random JWT and signature-seal keys (SR-SEC-04),
# and the database to use. SQL Server Express (instance SQLEXPRESS) when it is installed; otherwise a local
# demonstration database file, so PERA opens on any PC. Run again with -Force to rewrite.
param([switch]$Force)
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$file = Join-Path $root "api\appsettings.Production.json"
if ((Test-Path $file) -and -not $Force) { exit 0 }
function Key { $b = New-Object byte[] 48; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); [Convert]::ToBase64String($b) }
$sql = Get-Service -Name 'MSSQL$SQLEXPRESS' -ErrorAction SilentlyContinue
if ($sql) {
  $db = @{ Provider = "SqlServer" }
  $conn = "Server=.\SQLEXPRESS;Database=Pera;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=True"
  Write-Host "  Database: SQL Server Express (.\SQLEXPRESS), database 'Pera'"
} else {
  New-Item -ItemType Directory -Force -Path (Join-Path $root "data") | Out-Null
  $db = @{ Provider = "Sqlite" }
  $conn = "Data Source=" + (Join-Path $root "data\pera-demo.db")
  Write-Host "  SQL Server Express was not found - using the built-in DEMONSTRATION database (data\pera-demo.db)."
  Write-Host "  For real records install SQL Server Express (instance SQLEXPRESS) and run: powershell -File prepare-settings.ps1 -Force"
}
$settings = [ordered]@{
  ConnectionStrings = @{ Default = $conn }
  Database = $db
  Jwt = @{ Key = (Key) }
  Signing = @{ Key = (Key) }
  App = @{ UploadsDir = (Join-Path $root "uploads"); SeedDemo = $true; AllowedOrigins = @("http://localhost:3002"); SecureCookies = $false }
}
$settings | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 $file
