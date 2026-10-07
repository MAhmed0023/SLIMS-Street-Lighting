@echo off
REM ===========================================================================
REM  Installs SLIMS as a Windows service plus a startup task.
REM  Use this on a SERVER or VM, where SLIMS must keep running with nobody
REM  logged in. On an ordinary office PC use START-SLIMS.bat instead.
REM
REM  Put the folder somewhere outside a user profile first, e.g. C:\SLIMS -
REM  the service account cannot read files under C:\Users\<someone>.
REM
REM  RIGHT-CLICK this file and choose "Run as administrator".
REM ===========================================================================
setlocal
set HERE=%~dp0

net session >nul 2>&1
if errorlevel 1 (
  echo.
  echo   Administrator rights are required.
  echo   Right-click this file and choose "Run as administrator".
  echo.
  pause
  exit /b 1
)

echo.
echo   Installing SLIMS ...
echo.

REM --- stop anything started by START-SLIMS.bat ------------------------------
call "%HERE%STOP-SLIMS.bat" quiet

REM --- settings, uploads folder and file permissions for the service account -
powershell -NoProfile -ExecutionPolicy Bypass -File "%HERE%prepare-settings.ps1" -ApiDir "%HERE%api"
if not exist "%HERE%uploads" mkdir "%HERE%uploads"
icacls "%HERE%." /grant "*S-1-5-20:(OI)(CI)RX" /T /Q >nul
icacls "%HERE%uploads" /grant "*S-1-5-20:(OI)(CI)M" /T /Q >nul

REM --- SQL Server login for the service account ------------------------------
powershell -NoProfile -ExecutionPolicy Bypass -File "%HERE%grant-sql-access.ps1"
if errorlevel 1 (pause & exit /b 1)

REM --- API as a Windows service ----------------------------------------------
sc query SlimsApi >nul 2>&1 && (sc stop SlimsApi >nul & "%SystemRoot%\System32\timeout.exe" /t 3 /nobreak >nul & sc delete SlimsApi >nul)
sc create SlimsApi binPath= "\"%HERE%api\Slims.Api.exe\"" start= auto DisplayName= "SLIMS Street Lighting API" obj= "NT AUTHORITY\NetworkService"
sc description SlimsApi "SLIMS Street Lighting Store and Installation Management - application server (127.0.0.1:5091)"
sc failure SlimsApi reset= 86400 actions= restart/5000/restart/10000/restart/30000

REM --- web app: a Node process, so it runs as a scheduled task at boot ----------
schtasks /Delete /TN "SLIMS Web" /F >nul 2>&1
schtasks /Create /TN "SLIMS Web" /SC ONSTART /RU "SYSTEM" /RL HIGHEST /F /TR "\"%HERE%run-web.cmd\""

sc start SlimsApi
schtasks /Run /TN "SLIMS Web"

echo.
echo   Waiting for SLIMS to answer ...
set /a tries=0
:wait
"%SystemRoot%\System32\timeout.exe" /t 2 /nobreak >nul
set /a tries+=1
powershell -NoProfile -Command "try { (Invoke-WebRequest http://127.0.0.1:3001/login -UseBasicParsing -TimeoutSec 4).StatusCode } catch { 0 }" | find "200" >nul
if not errorlevel 1 goto up
if %tries% lss 45 goto wait
echo   SLIMS did not answer yet. Check the SlimsApi service in services.msc and web.log.
goto done
:up
echo   SLIMS is answering on port 3001.

:done
echo.
echo   ======================================================================
echo     Installed. Both parts now start automatically when the machine boots.
echo.
echo       Service         : SlimsApi     (127.0.0.1:5091)
echo       Scheduled task  : SLIMS Web    (port 3001)
echo.
echo     Next steps on a server:
echo       1. Run 1-open-firewall-RUN-AS-ADMIN.bat
echo       2. Put IIS or Nginx in front for HTTPS - see INSTALL.md
echo       3. Then set "SecureCookies": true in api\appsettings.Production.json
echo          and restart the SlimsApi service
echo.
echo     To remove:  sc stop SlimsApi  and  sc delete SlimsApi
echo                 schtasks /Delete /TN "SLIMS Web" /F
echo   ======================================================================
echo.
pause
