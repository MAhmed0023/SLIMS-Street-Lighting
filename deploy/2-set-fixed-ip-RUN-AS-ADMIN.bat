@echo off
REM Shows what the network administrator needs to keep this machine's IP address fixed,
REM so the office link to SLIMS does not change after a restart.
REM
REM If the address changes, everyone's bookmark stops working. Two ways to fix that, best first:
REM
REM   1. Ask the network administrator for a DHCP RESERVATION for this machine (preferred).
REM   2. Or set the address as static - only if the network admin confirms the address is free.
REM
REM This file changes nothing; it only reads the current settings.

powershell -NoProfile -Command ^
  "$c = Get-NetIPConfiguration | Where-Object { $_.IPv4Address -and $_.NetProfile.IPv4Connectivity -ne 'Disconnected' } | Select-Object -First 1; " ^
  "Write-Host ''; Write-Host '  Current network settings' -ForegroundColor Cyan; " ^
  "Write-Host ('  Adapter    : ' + $c.InterfaceAlias); " ^
  "Write-Host ('  IP address : ' + $c.IPv4Address.IPAddress); " ^
  "Write-Host ('  Gateway    : ' + $c.IPv4DefaultGateway.NextHop); " ^
  "Write-Host ('  MAC        : ' + (Get-NetAdapter -InterfaceAlias $c.InterfaceAlias).MacAddress); " ^
  "Write-Host ''; Write-Host '  Give the MAC address above to the network administrator and ask for' -ForegroundColor Yellow; " ^
  "Write-Host '  a DHCP reservation, so this machine always keeps the same IP address.' -ForegroundColor Yellow; " ^
  "Write-Host ('  SLIMS then stays at  http://' + $c.IPv4Address.IPAddress + ':3001') -ForegroundColor Yellow; Write-Host ''"

pause
