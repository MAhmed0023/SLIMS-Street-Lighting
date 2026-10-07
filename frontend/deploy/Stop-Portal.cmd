@echo off
REM Stops the SLIMS portal. Needed before replacing the build, because Windows keeps the
REM folder locked while the server is running.
schtasks /End /TN "SLIMS Portal" >nul 2>&1
powershell -NoProfile -Command "Get-NetTCPConnection -LocalPort 3001 -State Listen -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }"
echo.
echo Portal stopped.
"%SystemRoot%\System32\timeout.exe" /t 2 /nobreak >nul
