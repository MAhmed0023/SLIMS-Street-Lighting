@echo off
REM ===========================================================================
REM  Starts SLIMS - Street Lighting Store, Issuance and Installation Management -
REM  on this PC. Double-click this file. Keep the window open while people use it.
REM ===========================================================================
setlocal
set HERE=%~dp0
set NODE=%HERE%web\node.exe
if not exist "%NODE%" set NODE=node

title SLIMS Street Lighting - keep this window open

echo.
echo   Starting SLIMS ...
echo.

REM --- database check -------------------------------------------------------
sc query MSSQL$SQLEXPRESS | find "RUNNING" >nul
if errorlevel 1 (
  echo   SQL Server Express is not running. Trying to start it ...
  net start MSSQL$SQLEXPRESS >nul 2>&1
  sc query MSSQL$SQLEXPRESS | find "RUNNING" >nul
  if errorlevel 1 (
    echo.
    echo   SQL Server Express could not be started.
    echo   Install SQL Server Express first - see INSTALL.md
    echo.
    pause
    exit /b 1
  )
)

REM --- first start on this machine: its own settings and signing key --------
powershell -NoProfile -ExecutionPolicy Bypass -File "%HERE%prepare-settings.ps1" -ApiDir "%HERE%api"
if not exist "%HERE%uploads" mkdir "%HERE%uploads"

REM --- backend (started from its own folder so it finds its settings) -------
powershell -NoProfile -Command "try { (Invoke-WebRequest http://127.0.0.1:5091/api/health -UseBasicParsing -TimeoutSec 3).StatusCode } catch { 0 }" | find "200" >nul
if not errorlevel 1 goto apiok

start "SLIMS API" /min /D "%HERE%api" cmd /c ""%HERE%api\Slims.Api.exe" > "%HERE%api.log" 2>&1"

echo   Waiting for the database and API ^(first start builds the tables^) ...
set /a tries=0
:waitapi
"%SystemRoot%\System32\timeout.exe" /t 2 /nobreak >nul
set /a tries+=1
powershell -NoProfile -Command "try { (Invoke-WebRequest http://127.0.0.1:5091/api/health -UseBasicParsing -TimeoutSec 3).StatusCode } catch { 0 }" | find "200" >nul
if not errorlevel 1 goto apiok
if %tries% lss 45 goto waitapi
echo.
echo   The API did not start. Open api.log in this folder to see why.
echo.
pause
exit /b 1
:apiok
echo   API ready.

REM --- web app --------------------------------------------------------------
powershell -NoProfile -Command "try { (Invoke-WebRequest http://127.0.0.1:3001/login -UseBasicParsing -TimeoutSec 4).StatusCode } catch { 0 }" | find "200" >nul
if not errorlevel 1 goto webok

set PORT=3001
set HOSTNAME=0.0.0.0
set NODE_ENV=production
REM client-ip.cjs records each user's own machine in the audit trail
start "SLIMS Web" /min /D "%HERE%web" cmd /c ""%NODE%" -r "%HERE%client-ip.cjs" "%HERE%web\server.js" > "%HERE%web.log" 2>&1"

set /a tries=0
:waitweb
"%SystemRoot%\System32\timeout.exe" /t 2 /nobreak >nul
set /a tries+=1
powershell -NoProfile -Command "try { (Invoke-WebRequest http://127.0.0.1:3001/login -UseBasicParsing -TimeoutSec 4).StatusCode } catch { 0 }" | find "200" >nul
if not errorlevel 1 goto webok
if %tries% lss 45 goto waitweb
echo.
echo   The web app did not start. Open web.log in this folder to see why.
echo.
pause
exit /b 1
:webok

for /f %%i in ('powershell -NoProfile -Command "(Get-NetIPAddress -AddressFamily IPv4 ^| Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } ^| Select-Object -First 1).IPAddress"') do set IP=%%i

echo.
echo   ======================================================================
echo     SLIMS is running.
echo.
echo       On this PC      :  http://localhost:3001
echo       For the office  :  http://%IP%:3001
echo.
echo     First sign-in: user  sysadmin  - the password you type becomes
echo     the administrator password.
echo.
echo     Keep this window open. Closing it stops SLIMS,
echo     or run STOP-SLIMS.bat
echo   ======================================================================
echo.

start "" "http://localhost:3001"
echo   Press any key to STOP SLIMS and close.
pause >nul
call "%HERE%STOP-SLIMS.bat" quiet
