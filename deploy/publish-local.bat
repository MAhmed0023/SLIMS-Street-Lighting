@echo off
REM ===========================================================================
REM  Rebuilds the live SLIMS on THIS machine after a code change, and restarts it.
REM
REM    API      -> deploy\api                     (self-contained publish)
REM    Web app  -> frontend\.next\standalone       (production build)
REM
REM  The live copy shares the Slims database and the evidence uploads folder with
REM  development, so nothing is lost between the two. Double-click to run.
REM ===========================================================================
setlocal
set HERE=%~dp0
set ROOT=%HERE%..\

echo.
echo   [1/4] Stopping the live SLIMS ...
pythonw "%HERE%start_slims.pyw" --stop
"%SystemRoot%\System32\timeout.exe" /t 2 /nobreak >nul

echo   [2/4] Publishing the API ...
set KEEP=%TEMP%\slims-appsettings.Production.json
if exist "%HERE%api\appsettings.Production.json" copy /y "%HERE%api\appsettings.Production.json" "%KEEP%" >nul
dotnet publish "%ROOT%backend\src\Slims.Api" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o "%HERE%api" --nologo
if errorlevel 1 (echo   API BUILD FAILED & pause & exit /b 1)
REM first publish: this machine keeps its evidence files in the development uploads folder
if exist "%KEEP%" (
  move /y "%KEEP%" "%HERE%api\appsettings.Production.json" >nul
) else (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%HERE%prepare-settings.ps1" -ApiDir "%HERE%api" -UploadsDir "%ROOT%backend\src\Slims.Api\uploads"
)

echo   [3/4] Building the web app ...
pushd "%ROOT%frontend"
call npm run build
if errorlevel 1 (echo   WEB BUILD FAILED & popd & pause & exit /b 1)
popd

echo   [4/4] Starting SLIMS ...
start "" pythonw "%HERE%start_slims.pyw"
echo.
echo   Done. The browser opens by itself when SLIMS answers.
echo.
"%SystemRoot%\System32\timeout.exe" /t 5 /nobreak >nul
