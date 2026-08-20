# Iron Meridian — Linux / Steam Deck batch build
#
# Usage:
#   .\scripts\build-linux.ps1
#   .\scripts\build-linux.ps1 -Development
#   .\scripts\build-linux.ps1 -Clean
#
# SteamOS is Linux, so this is the native Steam Deck build. It is NOT the only
# way to support a Deck - the Windows player runs on one through Proton, and for
# a game with no anti-cheat that usually just works. See docs/42-STEAM-DECK.md
# section 2 for which one to ship.
param(
    # Defaults to the newest Unity 6000.x found under the Hub's editor folder.
    [string]$UnityPath,
    [string]$OutputDir = "Builds\Linux",
    [string]$ExeName = "IronMeridian.x86_64",
    # Development build: profiler attachable, deep stack traces.
    [switch]$Development,
    # Empty the output folder first.
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
$projectPath = (Resolve-Path "$PSScriptRoot\..").Path
$outPath = if ([System.IO.Path]::IsPathRooted($OutputDir)) { $OutputDir } else { Join-Path $projectPath $OutputDir }
$artifact = Join-Path $outPath $ExeName

# ------------------------------------------------------------------ the editor
# unity-run.ps1 owns editor discovery for every script in here.
if (-not $UnityPath) {
    $UnityPath = & (Join-Path $PSScriptRoot "unity-run.ps1") -PrintPath
}
if (-not $UnityPath -or -not (Test-Path $UnityPath)) {
    Write-Host "ERROR: Unity 6000.0 LTS not found. Pass -UnityPath ""...\Editor\Unity.exe""." -ForegroundColor Red
    exit 1
}

# Linux Build Support is an optional editor module, and without it the build
# fails several minutes in with a message about a missing target.
$editorDir = Split-Path $UnityPath
$playbackEngine = Join-Path $editorDir "Data\PlaybackEngines\LinuxStandaloneSupport"
if (-not (Test-Path $playbackEngine)) {
    Write-Host "ERROR: Linux Build Support is not installed for this editor." -ForegroundColor Red
    Write-Host "  Unity Hub -> Installs -> the 6000.0 editor -> Add modules ->" -ForegroundColor Yellow
    Write-Host "  Linux Build Support (IL2CPP)." -ForegroundColor Yellow
    exit 1
}

if ($Clean -and (Test-Path $outPath)) {
    Write-Host "Cleaning $outPath"
    Remove-Item -Recurse -Force $outPath
}
New-Item -ItemType Directory -Force -Path $outPath | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $projectPath "Builds") | Out-Null

Write-Host "Building Iron Meridian for Linux / Steam Deck -> $artifact"
Write-Host "  editor: $UnityPath"

# Setup first, same as every other build: the scene list AND
# StreamingAssets\Maps\index.json.
& $UnityPath `
    -batchmode -nographics -quit `
    -projectPath $projectPath `
    -executeMethod IronMeridian.EditorTools.ProjectBootstrap.SetupProject `
    -logFile (Join-Path $projectPath "Builds\setup-linux.log")
if ($LASTEXITCODE -ne 0) { throw "Scene setup failed - see Builds\setup-linux.log" }

$buildArgs = @(
    "-batchmode", "-nographics", "-quit",
    "-projectPath", $projectPath,
    "-buildTarget", "Linux64",
    "-executeMethod", "IronMeridian.EditorTools.LinuxBuild.BuildFromCommandLine",
    "-ironmeridian-output", $artifact,
    "-logFile", (Join-Path $projectPath "Builds\build-linux.log")
)
if ($Development) { $buildArgs += "-ironmeridian-development" }

& $UnityPath @buildArgs
if ($LASTEXITCODE -ne 0) { throw "Build failed - see Builds\build-linux.log" }
if (-not (Test-Path $artifact)) { throw "Build reported success but $artifact is not there." }

# Burst ships its debug symbols in a folder that says not to ship it.
Get-ChildItem -Path $outPath -Directory -Filter "*_DoNotShip" -ErrorAction SilentlyContinue |
    ForEach-Object { Remove-Item -Recurse -Force $_.FullName }

$sizeMb = [math]::Round(((Get-ChildItem $outPath -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Host "Done: $artifact ($sizeMb MB)"
Write-Host ""
Write-Host "The executable bit cannot be set from Windows - Steam sets it on install," -ForegroundColor Yellow
Write-Host "but a build copied to a Deck by hand needs: chmod +x $ExeName" -ForegroundColor Yellow
Write-Host "See docs/42-STEAM-DECK.md section 5." -ForegroundColor Yellow
