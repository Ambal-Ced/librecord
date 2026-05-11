@echo off
setlocal

rem LibRecord rebuild+start script (Windows)
rem - Rebuilds the project for testing new changes
rem - Starts the app in a separate terminal window
rem - Opens browser automatically to the app URL

cd /d "%~dp0"

set "APP_URL=http://localhost:5050"

echo Stopping any running LibRecord...
call "%~dp0stop-librecord.bat" >nul 2>&1

echo Rebuilding LibRecord...
dotnet build "LibRecord.csproj" -v minimal
if errorlevel 1 (
  echo Build failed. App not started.
  exit /b 1
)

echo Starting LibRecord on %APP_URL%...
start "LibRecord Server" cmd /k "cd /d ""%~dp0"" && dotnet run --no-build --project ""LibRecord.csproj"" --urls ""%APP_URL%"""

powershell -NoProfile -Command "Start-Sleep -Seconds 5"
start "" "%APP_URL%"

exit /b 0
