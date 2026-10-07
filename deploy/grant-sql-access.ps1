<#
  Lets the SLIMS API service sign in to SQL Server Express.

  The service runs as NT AUTHORITY\NETWORK SERVICE, which has no SQL login on a fresh SQL Express.
  This adds one, lets it create the Slims database on first start, and makes it owner of the
  database if it already exists. Runs as the administrator who installed SQL Express.
  Called by 3-install-as-service-RUN-AS-ADMIN.bat.
#>
param(
    [string] $Server   = ".\SQLEXPRESS",
    [string] $Database = "Slims",
    [string] $Login    = "NT AUTHORITY\NETWORK SERVICE"
)
$ErrorActionPreference = "Stop"

$sql = @"
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$Login')
    CREATE LOGIN [$Login] FROM WINDOWS;
ALTER SERVER ROLE dbcreator ADD MEMBER [$Login];
IF DB_ID(N'$Database') IS NOT NULL
BEGIN
    DECLARE @s nvarchar(max) = N'USE [$Database];
        IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N''$Login'')
            CREATE USER [$Login] FOR LOGIN [$Login];
        ALTER ROLE db_owner ADD MEMBER [$Login];';
    EXEC (@s);
END
"@

try {
    $cn = New-Object System.Data.SqlClient.SqlConnection("Server=$Server;Database=master;Integrated Security=True;TrustServerCertificate=True")
    $cn.Open()
    $cmd = $cn.CreateCommand(); $cmd.CommandText = $sql; [void]$cmd.ExecuteNonQuery()
    $cn.Close()
    Write-Host "  SQL Server access granted to $Login." -ForegroundColor Green
} catch {
    Write-Host "  Could not grant SQL Server access: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "  Sign in to SQL Server as an administrator and give $Login the dbcreator role." -ForegroundColor Yellow
    exit 1
}
