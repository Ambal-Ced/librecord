@echo off
setlocal enabledelayedexpansion

rem LibRecord stopper script (Windows)
rem - Stops the app using PID from App_Data\run\librecord.pid

cd /d "%~dp0"

set "RUN_DIR=%~dp0App_Data\run"
set "PID_FILE=%RUN_DIR%\librecord.pid"

if not exist "%PID_FILE%" (
  echo No PID file found at "%PID_FILE%".
  echo LibRecord may not be running.
  exit /b 0
)

for /f "usebackq delims=" %%P in ("%PID_FILE%") do set "PID=%%P"

if "%PID%"=="" (
  echo PID file is empty. Nothing to stop.
  del "%PID_FILE%" >nul 2>&1
  exit /b 0
)

echo Stopping LibRecord (PID %PID%)...

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$p = Get-Process -Id %PID% -ErrorAction SilentlyContinue;" ^
  "if ($null -eq $p) { Write-Host 'Not running.'; exit 0 }" ^
  "Stop-Process -Id %PID% -Force;" ^
  "Write-Host 'Stopped.'"

del "%PID_FILE%" >nul 2>&1
exit /b 0

