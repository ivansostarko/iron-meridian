# Iron Meridian — Windows batch build
#
# Usage:
#   .\scripts\build-windows.ps1
#   .\scripts\build-windows.ps1 -UnityPath "C:\Program Files\Unity\Hub\Editor\6000.0.80f1\Editor\Unity.exe"
#   .\scripts\build-windows.ps1 -Clean -Installer
#
# See docs/06-WINDOWS-BUILD.md.
param(
    # Defaults to the newest Unity 6000.x found under the Hub's editor folder.
    [string]$UnityPath,
    [string]$OutputDir = "Builds\Windows",
    [string]$ExeName = "IronMeridian.exe",
    # Empty the output folder first. Without it a rename of the product leaves
    # two players side by side and the installer has to guess between them.
    [switch]$Clean,
    # Package the result with Inno Setup afterwards (scripts\build-installer.ps1).
    [switch]$Installer,
    # Passed through to build-installer.ps1 — ships the Cesium ion token.
    [switch]$IncludeToken
)

$ErrorActionPreference = "Stop"
$projectPath = (Resolve-Path "$PSScriptRoot\..").Path
$outPath = if ([System.IO.Path]::IsPathRooted($OutputDir)) { $OutputDir } else { Join-Path $projectPath $OutputDir }

# ------------------------------------------------------------------ the editor
# unity-run.ps1 owns editor discovery for every script in here.
if (-not $UnityPath) {
    $UnityPath = & (Join-Path $PSScriptRoot "unity-run.ps1") -PrintPath
}
if (-not $UnityPath -or -not (Test-Path $UnityPath)) {
    Write-Host "ERROR: Unity 6000.0 LTS not found. Pass -UnityPath ""...\Editor\Unity.exe""." -ForegroundColor Red
    exit 1
}

if ($Clean -and (Test-Path $outPath)) {
    Write-Host "Cleaning $outPath"
    Remove-Item -Recurse -Force $outPath
}
New-Item -ItemType Directory -Force -Path $outPath | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $projectPath "Builds") | Out-Null

Write-Host "Building Iron Meridian -> $outPath"
Write-Host "  editor: $UnityPath"

& $UnityPath `
    -batchmode -nographics -quit `
    -projectPath $projectPath `
    -executeMethod IronMeridian.EditorTools.ProjectBootstrap.SetupProject `
    -logFile (Join-Path $projectPath "Builds\setup.log")
if ($LASTEXITCODE -ne 0) { throw "Scene setup failed — see Builds\setup.log" }

& $UnityPath `
    -batchmode -nographics -quit `
    -projectPath $projectPath `
    -buildWindows64Player (Join-Path $outPath $ExeName) `
    -logFile (Join-Path $projectPath "Builds\build.log")
if ($LASTEXITCODE -ne 0) { throw "Build failed — see Builds\build.log" }

# Burst ships its debug symbols in a folder that says not to ship it.
Get-ChildItem -Path $outPath -Directory -Filter "*_DoNotShip" -ErrorAction SilentlyContinue |
    ForEach-Object { Remove-Item -Recurse -Force $_.FullName }

Write-Host "Done: $outPath\$ExeName"

if ($Installer) {
    Write-Host ""
    & (Join-Path $PSScriptRoot "build-installer.ps1") -SourceDir $outPath -IncludeToken:$IncludeToken
    if ($LASTEXITCODE -ne 0) { throw "Installer packaging failed" }
}
