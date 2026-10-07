<#
  Writes api\appsettings.Production.json the first time SLIMS starts on a machine.

  Every installation gets its own random JWT signing key, so a copied package never shares a key
  with the machine it came from. An existing file is left alone - edit it by hand after that.
  Called by START-SLIMS.bat and 3-install-as-service-RUN-AS-ADMIN.bat.
#>
param(
    [Parameter(Mandatory = $true)][string] $ApiDir,
    # Where evidence photos and documents are kept. A relative path is taken from the API folder.
    [string] $UploadsDir = "..\uploads"
)

$target = Join-Path $ApiDir "appsettings.Production.json"
if (Test-Path $target) { exit 0 }

$bytes = New-Object byte[] 48
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$key = [Convert]::ToBase64String($bytes)
$uploads = $UploadsDir.TrimEnd('\').Replace('\', '\\')   # escaped for JSON

$json = @"
{
  "ConnectionStrings": {
    "Default": "Server=.\\SQLEXPRESS;Database=Slims;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=True"
  },
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://127.0.0.1:5091"
      }
    }
  },
  "Jwt": {
    "Issuer": "Slims",
    "Audience": "Slims",
    "Key": "$key",
    "AccessMinutes": 60,
    "RefreshDays": 7
  },
  "App": {
    "UploadsDir": "$uploads",
    "AllowedOrigins": [],
    "SecureCookies": false
  },
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Error"
    }
  },
  "AllowedHosts": "*"
}
"@
# UTF-8 without a byte-order mark
[System.IO.File]::WriteAllText($target, $json, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "  Settings written for this machine: $target"
