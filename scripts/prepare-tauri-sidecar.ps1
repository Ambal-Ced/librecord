Param(
  [string] $Configuration = "Release",
  [string] $Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$tauriRoot = Join-Path $repoRoot "src\librecord-tauri"
$binDir = Join-Path $tauriRoot "src-tauri\binaries"
$sidecarDir = Join-Path $binDir "server"

New-Item -ItemType Directory -Force -Path $sidecarDir | Out-Null

$publishOut = Join-Path $repoRoot "artifacts\tauri-server-publish"
if (Test-Path $publishOut) { Remove-Item -Recurse -Force $publishOut }

Write-Host "Publishing LibRecord server..."
dotnet publish (Join-Path $repoRoot "LibRecord.csproj") -c $Configuration -r $Runtime --self-contained true -o $publishOut | Out-Host

$srcExe = Join-Path $publishOut "LibRecord.exe"
if (!(Test-Path $srcExe)) { throw "Missing $srcExe after publish." }

# Refresh the bundled server directory with the full publish output so the runtime can
# resolve LibRecord.dll / deps.json / runtimeconfig.json / wwwroot, etc. next to the exe.
if (Test-Path $sidecarDir) { Remove-Item -Recurse -Force $sidecarDir }
New-Item -ItemType Directory -Force -Path $sidecarDir | Out-Null

Copy-Item -Recurse -Force (Join-Path $publishOut "*") $sidecarDir

Write-Host "Copied published server to $sidecarDir"

