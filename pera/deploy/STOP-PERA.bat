@echo off
REM Stops the PERA API (port 5092) and web app (port 3002) started by START-PERA.bat.
powershell -NoProfile -Command "foreach($p in 5092,3002){ Get-NetTCPConnection -LocalPort $p -State Listen -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue } }"
echo   PERA Workflow stopped.
timeout /t 2 >nul
