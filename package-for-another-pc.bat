@echo off
REM ===========================================================================
REM  Builds a complete, portable copy of SLIMS.
REM  The result folder can be copied to any Windows PC on a USB stick.
REM  Only SQL Server Express has to be installed on the other PC.
REM  Just double-click this file.
REM ===========================================================================
setlocal
set ROOT=%~dp0
set OUT=%ROOT%SLIMS-Setup
set NODEEXE=C:\Program Files\nodejs\node.exe

echo.
echo   Building the portable SLIMS package. This takes a few minutes.
echo.

REM The live SLIMS on this PC runs from frontend\.next\standalone, which the build replaces.
set WASLIVE=0
powershell -NoProfile -Command "if (Get-NetTCPConnection -LocalPort 3001 -State Listen -ErrorAction SilentlyContinue) { exit 1 }"
if errorlevel 1 (
  set WASLIVE=1
  echo   Stopping the live SLIMS on this PC for the build - it restarts afterwards.
  pythonw "%ROOT%deploy\start_slims.pyw" --stop
  "%SystemRoot%\System32\timeout.exe" /t 2 /nobreak >nul
)

if exist "%OUT%" rmdir /s /q "%OUT%"
if exist "%OUT%" (echo   %OUT% is in use - close SLIMS-Setup windows first & pause & exit /b 1)
mkdir "%OUT%"

echo   [1/4] Backend (self-contained, no .NET install needed on the other PC) ...
dotnet publish "%ROOT%backend\src\Slims.Api" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o "%OUT%\api" --nologo
if errorlevel 1 (echo   BACKEND BUILD FAILED & pause & exit /b 1)

echo   [2/4] Web app (standalone server) ...
pushd "%ROOT%frontend"
call npm run build
if errorlevel 1 (echo   WEB BUILD FAILED - if SLIMS is running on this PC, stop it first & popd & pause & exit /b 1)
popd
REM npm run build already copied .next\static and public into the standalone folder
xcopy /e /i /q /y "%ROOT%frontend\.next\standalone\*" "%OUT%\web\" >nul

echo   [3/4] Node runtime and start files ...
if exist "%NODEEXE%" copy /y "%NODEEXE%" "%OUT%\web\node.exe" >nul
for %%f in (slims.ico START-SLIMS.bat STOP-SLIMS.bat run-web.cmd client-ip.cjs prepare-settings.ps1 grant-sql-access.ps1 ^
            1-open-firewall-RUN-AS-ADMIN.bat 2-set-fixed-ip-RUN-AS-ADMIN.bat 3-install-as-service-RUN-AS-ADMIN.bat) do (
  copy /y "%ROOT%deploy\%%f" "%OUT%\" >nul
)
copy /y "%ROOT%INSTALL.md" "%OUT%\INSTALL.md" >nul
mkdir "%OUT%\uploads"
REM the SLIMS Field app, served to phones from Mobile Setup (build it first with mobile\build-apk.bat)
set APK=%ROOT%mobile\android\app\build\outputs\apk\release\app-release.apk
if exist "%APK%" (
  mkdir "%OUT%\api\downloads" 2>nul
  copy /y "%APK%" "%OUT%\api\downloads\slims-field.apk" >nul
  if exist "%ROOT%deploy\api\downloads\slims-field.json" copy /y "%ROOT%deploy\api\downloads\slims-field.json" "%OUT%\api\downloads\" >nul
) else echo   Note: no field app APK found - run mobile\build-apk.bat to include it.

echo   [4/4] Clearing this machine's settings from the copy ...
REM START-SLIMS.bat writes fresh settings with a new signing key on the other PC.
if exist "%OUT%\api\appsettings.Production.json" del /q "%OUT%\api\appsettings.Production.json"
if exist "%OUT%\api\appsettings.Development.json" del /q "%OUT%\api\appsettings.Development.json"
if exist "%OUT%\api\uploads" rmdir /s /q "%OUT%\api\uploads"

if "%WASLIVE%"=="1" (
  echo   Restarting the live SLIMS on this PC ...
  start "" pythonw "%ROOT%deploy\start_slims.pyw" --no-browser
)

echo.
echo   ======================================================================
echo     Done.  Copy this whole folder to the other PC:
echo.
echo        %OUT%
echo.
echo     Then on that PC read INSTALL.md and run START-SLIMS.bat
echo   ======================================================================
echo.
if not "%1"=="nopause" pause
