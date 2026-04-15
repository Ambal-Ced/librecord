@echo off
setlocal enabledelayedexpansion

rem LibRecord starter script (Windows)
rem - Starts the app without the IDE
rem - Writes PID to App_Data\run\librecord.pid
rem - Writes logs to App_Data\run\librecord.log

cd /d "%~dp0"

set "RUN_DIR=%~dp0App_Data\run"
set "PID_FILE=%RUN_DIR%\librecord.pid"
set "LOG_FILE=%RUN_DIR%\librecord.log"

if not exist "%RUN_DIR%" mkdir "%RUN_DIR%" >nul 2>&1

if exist "%PID_FILE%" (
  for /f "usebackq delims=" %%P in ("%PID_FILE%") do set "OLDPID=%%P"
  if not "%OLDPID%"=="" (
    powershell -NoProfile -Command "if (Get-Process -Id %OLDPID% -ErrorAction SilentlyContinue) { exit 0 } else { exit 1 }"
    if "!errorlevel!"=="0" (
      echo LibRecord already running (PID !OLDPID!).
      echo Log: "%LOG_FILE%"
      exit /b 0
    )
  )
)

echo Starting LibRecord...
echo Log: "%LOG_FILE%"

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference='Stop';" ^
  "New-Item -ItemType Directory -Force -Path '%RUN_DIR%' | Out-Null;" ^
  "$p = Start-Process -FilePath 'dotnet' -ArgumentList @('run','--project','LibRecord.csproj','--urls','http://localhost:5050') -WorkingDirectory '%~dp0' -WindowStyle Minimized -RedirectStandardOutput '%LOG_FILE%' -RedirectStandardError '%LOG_FILE%' -PassThru;" ^
  "Set-Content -Path '%PID_FILE%' -Value $p.Id -Encoding ascii;" ^
  "Write-Host ('Started. PID ' + $p.Id + '. Browse http://localhost:5050')"

exit /b %errorlevel%

