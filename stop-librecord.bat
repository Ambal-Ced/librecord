@echo off
setlocal

rem LibRecord stopper script (Windows)
rem - Matches start-librecord.bat: same URL/port and "LibRecord Server" window title
rem - Stops the server window, then any dotnet still running this project

cd /d "%~dp0"

set "APP_URL=http://localhost:5050"
set "APP_PORT=5050"

echo Stopping LibRecord (matches start: %APP_URL%)...

rem Kill the console started by start-librecord.bat (window title must match exactly)
taskkill /FI "WINDOWTITLE eq LibRecord Server*" /T /F >nul 2>&1

rem If the window was renamed or only dotnet remains, stop dotnet running this .csproj
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Get-CimInstance Win32_Process -Filter \"Name='dotnet.exe'\" -ErrorAction SilentlyContinue |" ^
  "Where-Object { $_.CommandLine -like '*LibRecord.csproj*' } |" ^
  "ForEach-Object { try { Stop-Process -Id $_.ProcessId -Force -ErrorAction Stop; Write-Host ('Stopped dotnet PID ' + $_.ProcessId) } catch {} }"

rem Release port if something is still listening (same port as --urls in start script)
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "try { Get-NetTCPConnection -LocalPort %APP_PORT% -State Listen -ErrorAction SilentlyContinue |" ^
  "ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue } } catch {}"

rem Old versions wrote App_Data\run\librecord.pid; remove stale file if present
set "OLD_PID=%~dp0App_Data\run\librecord.pid"
if exist "%OLD_PID%" del "%OLD_PID%" >nul 2>&1

echo Done.
exit /b 0
