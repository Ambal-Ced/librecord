Param(
  [string] $Configuration = "Release",
  [string] $Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$packageDir = Join-Path $repoRoot "src\LibRecord.Package"
$serverOut = Join-Path $packageDir "Server"
$wv2Out = Join-Path $packageDir "WebView2Runtime"

Write-Host "Publishing server to $serverOut"
if (Test-Path $serverOut) { Remove-Item -Recurse -Force $serverOut }
dotnet publish (Join-Path $repoRoot "LibRecord.csproj") -c $Configuration -r $Runtime --self-contained true -o $serverOut

if (!(Test-Path $wv2Out)) {
  Write-Host "WebView2 fixed runtime not found at $wv2Out"
  Write-Host "Download it (x64) and extract into that folder so it contains msedgewebview2.exe."
  Write-Host "Docs: https://learn.microsoft.com/microsoft-edge/webview2/concepts/distribution"
  throw "Missing WebView2Runtime folder. See message above."
}

Write-Host "Building desktop app"
dotnet build (Join-Path $repoRoot "src\LibRecord.Desktop\LibRecord.Desktop.csproj") -c $Configuration

Write-Host "Building MSIX (requires Visual Studio / Desktop Bridge targets)"
dotnet msbuild (Join-Path $repoRoot "src\LibRecord.Package\LibRecord.Package.wapproj") /p:Configuration=$Configuration

