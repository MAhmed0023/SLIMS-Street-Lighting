@echo off
REM Opens port 3001 so the office can reach SLIMS on this machine.
REM RIGHT-CLICK this file and choose "Run as administrator".
REM Only the Private and Domain network profiles are opened, so a public Wi-Fi stays closed.
REM The API port 5091 stays closed: it listens on 127.0.0.1 and only the web app talks to it.

net session >nul 2>&1
if errorlevel 1 (
  echo.
  echo   Administrator rights are required.
  echo   Close this window, right-click the file and choose "Run as administrator".
  echo.
  pause
  exit /b 1
)

powershell -NoProfile -Command ^
  "Get-NetFirewallRule -DisplayName 'SLIMS Street Lighting (office access)' -ErrorAction SilentlyContinue | Remove-NetFirewallRule; " ^
  "New-NetFirewallRule -DisplayName 'SLIMS Street Lighting (office access)' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 3001 -Profile Private,Domain -Description 'SLIMS Street Lighting Store and Installation Management - office LAN only' | Out-Null; " ^
  "Write-Host ''; Write-Host '  Port 3001 opened for the office network.' -ForegroundColor Green; " ^
  "$pub = Get-NetConnectionProfile | Where-Object { $_.NetworkCategory -eq 'Public' }; " ^
  "if ($pub) { Write-Host ('  Note: the network ' + $pub.Name + ' is marked Public, where this rule does not apply.') -ForegroundColor Yellow; Write-Host '  Ask IT to mark the office network as Private or Domain.' -ForegroundColor Yellow }; " ^
  "$ip = (Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } | Select-Object -First 1).IPAddress; " ^
  "Write-Host ''; Write-Host ('  Colleagues open:  http://' + $ip + ':3001') -ForegroundColor Yellow; Write-Host ''"

echo   To close it again later, run this command as administrator:
echo   powershell -Command "Remove-NetFirewallRule -DisplayName 'SLIMS Street Lighting (office access)'"
echo.
pause
