# Iron Meridian — Windows batch build
# Usage:
#   .\scripts\build-windows.ps1 -UnityPath "C:\Program Files\Unity\Hub\Editor\6000.0.80f1\Editor\Unity.exe"
param(
    [Parameter(Mandatory = $true)]
    [string]$UnityPath,
    [string]$OutputDir = "Builds\Windows"
)

$ErrorActionPreference = "Stop"
$projectPath = (Resolve-Path "$PSScriptRoot\..").Path
$outPath = Join-Path $projectPath $OutputDir
New-Item -ItemType Directory -Force -Path $outPath | Out-Null

Write-Host "Building Iron Meridian -> $outPath"

& $UnityPath `
    -batchmode -nographics -quit `
    -projectPath $projectPath `
    -executeMethod IronMeridian.EditorTools.ProjectBootstrap.SetupProject `
    -logFile (Join-Path $projectPath "Builds\setup.log")
if ($LASTEXITCODE -ne 0) { throw "Scene setup failed — see Builds\setup.log" }

& $UnityPath `
    -batchmode -nographics -quit `
    -projectPath $projectPath `
    -buildWindows64Player (Join-Path $outPath "IronMeridian.exe") `
    -logFile (Join-Path $projectPath "Builds\build.log")
if ($LASTEXITCODE -ne 0) { throw "Build failed — see Builds\build.log" }

Write-Host "Done: $outPath\IronMeridian.exe"
