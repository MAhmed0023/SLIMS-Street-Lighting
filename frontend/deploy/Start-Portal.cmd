@echo off
REM Starts the SLIMS portal. Normally Windows does this at startup through the scheduled task;
REM use this only to bring it back up by hand.
schtasks /Run /TN "SLIMS Portal"
if errorlevel 1 (
  echo.
  echo Could not start the task. Has Deploy-Portal.ps1 been run on this server?
  pause
  exit /b 1
)
echo.
echo Portal starting. Give it a few seconds, then open the SLIMS address in a browser.
"%SystemRoot%\System32\timeout.exe" /t 3 /nobreak >nul
