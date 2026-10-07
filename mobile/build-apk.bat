@echo off
REM ===========================================================================
REM  Builds the SLIMS Field Android app (signed release APK) and publishes it to
REM  the live SLIMS on this PC, so phones can download it from Mobile Setup.
REM
REM  Needs the free build tools in C:\Users\User\android-dev (JDK 21 + Android SDK 35).
REM  The signing key is mobile\keystore - back it up; every update must use it.
REM ===========================================================================
setlocal
set HERE=%~dp0
set JAVA_HOME=C:\Users\User\android-dev\jdk-21
set ANDROID_HOME=C:\Users\User\android-dev\sdk
set ANDROID_SDK_ROOT=%ANDROID_HOME%
set PATH=%JAVA_HOME%\bin;%PATH%

echo   [1/4] Web bundle ...
pushd "%HERE%"
call npm run build
if errorlevel 1 (echo   BUILD FAILED & popd & pause & exit /b 1)
echo   [2/4] Copying into the Android project ...
call npx cap sync android
if errorlevel 1 (echo   SYNC FAILED & popd & pause & exit /b 1)
echo   [3/4] Signed release APK ...
pushd android
call "%HERE%android\gradlew.bat" assembleRelease --console=plain
if errorlevel 1 (echo   GRADLE FAILED & popd & popd & pause & exit /b 1)
popd
popd

echo   [4/4] Publishing to the live SLIMS ...
set APK=%HERE%android\app\build\outputs\apk\release\app-release.apk
set DEST=%HERE%..\deploy\api\downloads
if not exist "%DEST%" mkdir "%DEST%"
copy /y "%APK%" "%DEST%\slims-field.apk" >nul
for /f "tokens=2 delims=:, " %%v in ('findstr /c:"\"version\"" "%HERE%package.json"') do echo %%~v> "%DEST%\slims-field.json"
echo.
echo   Done: %DEST%\slims-field.apk
echo   Phones download it from Settings - Mobile Setup on the portal.
echo.
if not "%1"=="nopause" pause
