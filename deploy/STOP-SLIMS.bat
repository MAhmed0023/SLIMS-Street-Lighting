@echo off
REM Stops SLIMS on this PC. Only the SLIMS ports 3001 and 5091 are touched,
REM so PIMRS on 3000 and 5090 keeps running.
taskkill /F /IM Slims.Api.exe >nul 2>&1
powershell -NoProfile -Command "Get-NetTCPConnection -LocalPort 3001,5091 -State Listen -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }" >nul 2>&1
if "%1"=="quiet" exit /b 0
echo.
echo   SLIMS stopped.
echo.
"%SystemRoot%\System32\timeout.exe" /t 2 /nobreak >nul
