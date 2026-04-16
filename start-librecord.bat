@echo off
setlocal

rem LibRecord starter script (Windows)
rem - Starts the app in a separate terminal window
rem - Opens browser automatically to the app URL

cd /d "%~dp0"

set "APP_URL=http://localhost:5050"

echo Starting LibRecord on %APP_URL%...
start "LibRecord Server" cmd /k "cd /d ""%~dp0"" && dotnet run --project ""LibRecord.csproj"" --urls ""%APP_URL%"""

powershell -NoProfile -Command "Start-Sleep -Seconds 5"
start "" "%APP_URL%"

exit /b 0

