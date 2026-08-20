# Iron Meridian — Android batch build
#
# Usage:
#   .\scripts\build-android.ps1
#   .\scripts\build-android.ps1 -Aab                 # App Bundle for Play, not an APK
#   .\scripts\build-android.ps1 -Development         # profiler attachable
#   .\scripts\build-android.ps1 -Install             # adb install onto the attached device
#   .\scripts\build-android.ps1 -Keystore ".\keys\release.keystore" -KeystorePass $env:IM_KS_PASS `
#                               -KeyAlias ironmeridian -KeyPass $env:IM_KEY_PASS
#
# The signing arguments are NEVER defaulted and never written anywhere. Unsigned,
# Unity signs with its debug key, which installs on a device and is rejected by
# Play — which is the right way round for a build you run by accident.
#
# See docs/40-ANDROID.md.
param(
    # Defaults to the newest Unity 6000.x found under the Hub's editor folder.
    [string]$UnityPath,
    [string]$OutputDir = "Builds\Android",
    [string]$FileName,
    # Android App Bundle (.aab) for Play, instead of an installable .apk.
    [switch]$Aab,
    # Development build: profiler, deep stack traces, script debugging.
    [switch]$Development,
    # Empty the output folder first.
    [switch]$Clean,
    # adb install -r the result when it is done.
    [switch]$Install,
    # Release signing. All four or none.
    [string]$Keystore,
    [string]$KeystorePass,
    [string]$KeyAlias,
    [string]$KeyPass
)

$ErrorActionPreference = "Stop"
$projectPath = (Resolve-Path "$PSScriptRoot\..").Path
$outPath = if ([System.IO.Path]::IsPathRooted($OutputDir)) { $OutputDir } else { Join-Path $projectPath $OutputDir }

if (-not $FileName) {
    $FileName = if ($Aab) { "IronMeridian.aab" } else { "IronMeridian.apk" }
}
$artifact = Join-Path $outPath $FileName

# ------------------------------------------------------------------ the editor
# unity-run.ps1 owns editor discovery for every script in here.
if (-not $UnityPath) {
    $UnityPath = & (Join-Path $PSScriptRoot "unity-run.ps1") -PrintPath
}
if (-not $UnityPath -or -not (Test-Path $UnityPath)) {
    Write-Host "ERROR: Unity 6000.0 LTS not found. Pass -UnityPath ""...\Editor\Unity.exe""." -ForegroundColor Red
    exit 1
}

# ------------------------------------------------------------ the Android tools
# Unity ships its own OpenJDK, SDK and NDK as optional modules. Without the
# Android Build Support module the build fails several minutes in with a message
# about a missing target, so it is worth saying so now.
$editorDir = Split-Path $UnityPath
$playbackEngine = Join-Path $editorDir "Data\PlaybackEngines\AndroidPlayer"
if (-not (Test-Path $playbackEngine)) {
    Write-Host "ERROR: Android Build Support is not installed for this editor." -ForegroundColor Red
    Write-Host "  Unity Hub -> Installs -> the 6000.0 editor -> Add modules ->" -ForegroundColor Yellow
    Write-Host "  Android Build Support, plus its OpenJDK and Android SDK & NDK Tools." -ForegroundColor Yellow
    exit 1
}

# ------------------------------------------------------------------- signing
$signingArgs = @()
$given = @($Keystore, $KeystorePass, $KeyAlias, $KeyPass) | Where-Object { $_ }
if ($given.Count -gt 0 -and $given.Count -lt 4) {
    Write-Host "ERROR: give all four of -Keystore -KeystorePass -KeyAlias -KeyPass, or none." -ForegroundColor Red
    exit 1
}
if ($given.Count -eq 4) {
    if (-not (Test-Path $Keystore)) { Write-Host "ERROR: keystore not found: $Keystore" -ForegroundColor Red; exit 1 }
    $signingArgs = @(
        "-keystorePath", (Resolve-Path $Keystore).Path,
        "-keystorePass", $KeystorePass,
        "-keyaliasName", $KeyAlias,
        "-keyaliasPass", $KeyPass
    )
    Write-Host "Signing with $Keystore (alias $KeyAlias)"
} else {
    Write-Host "No keystore given - Unity will sign with its debug key." -ForegroundColor Yellow
    Write-Host "  That installs on a device and is rejected by Play." -ForegroundColor Yellow
}

if ($Clean -and (Test-Path $outPath)) {
    Write-Host "Cleaning $outPath"
    Remove-Item -Recurse -Force $outPath
}
New-Item -ItemType Directory -Force -Path $outPath | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $projectPath "Builds") | Out-Null

Write-Host "Building Iron Meridian for Android -> $artifact"
Write-Host "  editor: $UnityPath"

# Setup first, same as the Windows build: it writes the scene list AND
# StreamingAssets\Maps\index.json, which Android needs because it cannot list a
# directory inside the APK. See docs\40-ANDROID.md.
& $UnityPath `
    -batchmode -nographics -quit `
    -projectPath $projectPath `
    -executeMethod IronMeridian.EditorTools.ProjectBootstrap.SetupProject `
    -logFile (Join-Path $projectPath "Builds\setup-android.log")
if ($LASTEXITCODE -ne 0) { throw "Scene setup failed - see Builds\setup-android.log" }

$buildArgs = @(
    "-batchmode", "-nographics", "-quit",
    "-projectPath", $projectPath,
    "-buildTarget", "Android",
    "-executeMethod", "IronMeridian.EditorTools.AndroidBuild.BuildFromCommandLine",
    "-ironmeridian-output", $artifact,
    "-logFile", (Join-Path $projectPath "Builds\build-android.log")
)
if ($Aab) { $buildArgs += "-ironmeridian-aab" }
if ($Development) { $buildArgs += "-ironmeridian-development" }
$buildArgs += $signingArgs

& $UnityPath @buildArgs
if ($LASTEXITCODE -ne 0) { throw "Build failed - see Builds\build-android.log" }

if (-not (Test-Path $artifact)) { throw "Build reported success but $artifact is not there." }

$sizeMb = [math]::Round((Get-Item $artifact).Length / 1MB, 1)
Write-Host "Done: $artifact ($sizeMb MB)"

# ------------------------------------------------------------------- install
if ($Install) {
    if ($Aab) {
        Write-Host "An .aab cannot be installed directly - use bundletool, or build without -Aab." -ForegroundColor Yellow
        exit 0
    }

    $adb = (Get-Command adb -ErrorAction SilentlyContinue)?.Source
    if (-not $adb) {
        $sdkAdb = Join-Path $playbackEngine "SDK\platform-tools\adb.exe"
        if (Test-Path $sdkAdb) { $adb = $sdkAdb }
    }
    if (-not $adb) {
        Write-Host "adb not found - install Android SDK Platform Tools, or add it to PATH." -ForegroundColor Yellow
        exit 0
    }

    Write-Host "Installing onto the attached device..."
    & $adb install -r $artifact
    if ($LASTEXITCODE -ne 0) { throw "adb install failed" }
    Write-Host "Installed."
}
