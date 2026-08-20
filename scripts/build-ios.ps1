# Iron Meridian — iOS Xcode project export
#
# Usage:
#   .\scripts\build-ios.ps1
#   .\scripts\build-ios.ps1 -Development
#   .\scripts\build-ios.ps1 -Clean
#
# THIS DOES NOT PRODUCE AN APP. Unity's iOS target produces an Xcode project, on
# every host Unity runs on; turning that into a signed .ipa needs Xcode, which
# needs macOS. Running it on Windows is still worth doing - it is what catches a
# missing module, an IL2CPP failure or a stripping error before a Mac is
# involved - but the last mile is a Mac. See docs/43-IOS.md section 5.
param(
    # Defaults to the newest Unity 6000.x found under the Hub's editor folder.
    [string]$UnityPath,
    [string]$OutputDir = "Builds\iOS",
    # Development build: profiler attachable, deep stack traces.
    [switch]$Development,
    # Empty the output folder first. Worth doing after changing player settings:
    # Unity appends to an existing Xcode project rather than replacing it.
    [switch]$Clean
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

# iOS Build Support is an optional editor module. It also carries
# UnityEditor.iOS.Xcode, which the Info.plist post-process needs.
$editorDir = Split-Path $UnityPath
$playbackEngine = Join-Path $editorDir "Data\PlaybackEngines\iOSSupport"
if (-not (Test-Path $playbackEngine)) {
    Write-Host "ERROR: iOS Build Support is not installed for this editor." -ForegroundColor Red
    Write-Host "  Unity Hub -> Installs -> the 6000.0 editor -> Add modules -> iOS Build Support." -ForegroundColor Yellow
    exit 1
}

if ($Clean -and (Test-Path $outPath)) {
    Write-Host "Cleaning $outPath"
    Remove-Item -Recurse -Force $outPath
}
New-Item -ItemType Directory -Force -Path $outPath | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $projectPath "Builds") | Out-Null

Write-Host "Exporting the Iron Meridian Xcode project -> $outPath"
Write-Host "  editor: $UnityPath"

# Setup first, same as every other build: the scene list AND
# StreamingAssets\Maps\index.json.
& $UnityPath `
    -batchmode -nographics -quit `
    -projectPath $projectPath `
    -executeMethod IronMeridian.EditorTools.ProjectBootstrap.SetupProject `
    -logFile (Join-Path $projectPath "Builds\setup-ios.log")
if ($LASTEXITCODE -ne 0) { throw "Scene setup failed - see Builds\setup-ios.log" }

$buildArgs = @(
    "-batchmode", "-nographics", "-quit",
    "-projectPath", $projectPath,
    "-buildTarget", "iOS",
    "-executeMethod", "IronMeridian.EditorTools.IosBuild.BuildFromCommandLine",
    "-ironmeridian-output", $outPath,
    "-logFile", (Join-Path $projectPath "Builds\build-ios.log")
)
if ($Development) { $buildArgs += "-ironmeridian-development" }

& $UnityPath @buildArgs
if ($LASTEXITCODE -ne 0) { throw "Export failed - see Builds\build-ios.log" }

$xcodeproj = Join-Path $outPath "Unity-iPhone.xcodeproj"
if (-not (Test-Path $xcodeproj)) {
    throw "Export reported success but there is no Unity-iPhone.xcodeproj in $outPath."
}

$sizeMb = [math]::Round(((Get-ChildItem $outPath -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Host "Done: $outPath ($sizeMb MB)"
Write-Host ""
Write-Host "This is an Xcode project, not an app. On a Mac:" -ForegroundColor Cyan
Write-Host "  1. copy Builds\iOS across (or share it - keep the folder intact)"
Write-Host "  2. open Unity-iPhone.xcodeproj"
Write-Host "  3. set your Team under Signing & Capabilities"
Write-Host "  4. Product > Archive, then Distribute App"
Write-Host ""
Write-Host "Do NOT hand-edit Info.plist in Xcode - the project is regenerated on every" -ForegroundColor Yellow
Write-Host "export and the edit is silently lost. Add the key to IosBuild.OnPostprocessBuild." -ForegroundColor Yellow
Write-Host "docs/43-IOS.md section 6." -ForegroundColor Yellow

# ------------------------------------------------------------------ the token
# StreamingAssets ships inside the .app as plain files, so an .ipa anyone can
# sideload carries the token with it. docs/43-IOS.md section 7.
$token = Join-Path $outPath "Data\Raw\cesium-token.txt"
if (-not (Test-Path $token)) { $token = Join-Path $outPath "Data\cesium-token.txt" }
if (Test-Path $token) {
    $content = (Get-Content $token -Raw).Trim()
    if ($content -and $content -notmatch '^PASTE_YOUR') {
        Write-Host ""
        Write-Host "WARNING: this export carries your Cesium ion token." -ForegroundColor Yellow
        Write-Host "  It ships inside the .app as a plain file. Use a token restricted to" -ForegroundColor Yellow
        Write-Host "  asset read on the tilesets, and be ready to revoke it." -ForegroundColor Yellow
    }
}
