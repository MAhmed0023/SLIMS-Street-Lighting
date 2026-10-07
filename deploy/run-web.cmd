@echo off
REM Runs the SLIMS web app in this window. Used by the "SLIMS Web" startup task that
REM 3-install-as-service-RUN-AS-ADMIN.bat creates; people normally use START-SLIMS.bat instead.
setlocal
set NODE=%~dp0web\node.exe
if not exist "%NODE%" set NODE=node
set NODE_ENV=production
set PORT=3001
set HOSTNAME=0.0.0.0
cd /d "%~dp0web"
REM client-ip.cjs records each user's own machine in the audit trail
"%NODE%" -r "%~dp0client-ip.cjs" server.js >> "%~dp0web.log" 2>&1
