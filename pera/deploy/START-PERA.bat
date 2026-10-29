@echo off
REM ===========================================================================
REM  Starts PERA Workflow on this PC:  API on port 5092, web app on port 3002.
REM  Open http://localhost:3002  (other PCs: http://<this PC's IP>:3002)
REM ===========================================================================
setlocal
set ROOT=%~dp0
cd /d "%ROOT%"
echo.
echo   Starting PERA Workflow ...
powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%prepare-settings.ps1"
if errorlevel 1 (echo   Could not write the settings file. & pause & exit /b 1)

set ASPNETCORE_ENVIRONMENT=Production
start "PERA API" /min /d "%ROOT%api" "%ROOT%api\Pera.Api.exe"

echo   Waiting for the API (the first start creates the database) ...
powershell -NoProfile -Command "$t=0; while($t -lt 90){ try { Invoke-WebRequest -UseBasicParsing http://localhost:5092/api/health -TimeoutSec 2 | Out-Null; exit 0 } catch { Start-Sleep 1; $t++ } }; exit 1"
if errorlevel 1 (echo   The API did not start. Look at the "PERA API" window for the error. & pause & exit /b 1)

set PORT=3002
set HOSTNAME=0.0.0.0
start "PERA Web" /min /d "%ROOT%web" "%ROOT%web\node.exe" server.js
powershell -NoProfile -Command "$t=0; while($t -lt 30){ try { Invoke-WebRequest -UseBasicParsing http://localhost:3002/login -TimeoutSec 2 | Out-Null; exit 0 } catch { Start-Sleep 1; $t++ } }; exit 1"

start "" http://localhost:3002
echo.
echo   ======================================================================
echo     PERA Workflow is running:   http://localhost:3002
echo     API documentation:          http://localhost:5092/swagger
echo.
echo     First sign-in: user  sysadmin  - the password you type becomes the
echo     administrator password. Then issue passwords under
echo     Settings ^> Users ^& Access.   Stop with STOP-PERA.bat
echo   ======================================================================
echo.
pause
