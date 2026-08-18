# Iron Meridian — package a built Windows player as a setup .exe
#
# Wraps Inno Setup around whatever scripts\build-windows.ps1 (or the editor)
# produced. It does not build the game; point it at a player folder.
#
# Usage:
#   .\scripts\build-installer.ps1
#   .\scripts\build-installer.ps1 -SourceDir "Builds\Windows" -Version 1.1
#   .\scripts\build-installer.ps1 -IncludeToken          # ships the ion token
#
# See docs/34-INSTALLER.md.
param(
    # Folder holding the built player (the one with the .exe and *_Data).
    [string]$SourceDir = "Builds\Windows",
    # Where the setup .exe is written.
    [string]$OutputDir = "Builds\Installer",
    # Defaults to bundleVersion from ProjectSettings.asset.
    [string]$Version,
    # Defaults to the installed Inno Setup 6.
    [string]$IsccPath,
    # Bundle Assets/StreamingAssets/cesium-token.txt with the game. Off by
    # default: the token is a secret and travels with anyone you hand the
    # installer to (golden rule 1).
    [switch]$IncludeToken,
    # Authenticode signing, e.g.
    #   -SignToolCommand 'signtool.exe sign /fd sha256 /a $f'
    [string]$SignToolCommand
)

$ErrorActionPreference = "Stop"
$projectPath = (Resolve-Path "$PSScriptRoot\..").Path
$installerDir = Join-Path $projectPath "installer"

function Fail($message) { Write-Host "ERROR: $message" -ForegroundColor Red; exit 1 }

# ---------------------------------------------------------------- the player
if (-not [System.IO.Path]::IsPathRooted($SourceDir)) {
    $SourceDir = Join-Path $projectPath $SourceDir
}
if (-not (Test-Path $SourceDir)) {
    Fail "No player build at $SourceDir. Run .\scripts\build-windows.ps1 first."
}
$SourceDir = (Resolve-Path $SourceDir).Path.TrimEnd('\')

# Unity names the .exe after the product, and the build script may override it,
# so find it rather than assuming: the one whose *_Data folder sits beside it.
$players = @(Get-ChildItem -Path $SourceDir -Filter *.exe |
    Where-Object { Test-Path (Join-Path $SourceDir "$($_.BaseName)_Data") } |
    Sort-Object LastWriteTime -Descending)
if ($players.Count -eq 0) {
    Fail "No Unity player found in $SourceDir (expected an .exe next to a matching *_Data folder)."
}
if ($players.Count -gt 1) {
    # Two builds under different product names share one folder — packaging the
    # stale one is silent and undebuggable, so say which was chosen.
    Write-Host "WARNING: $($players.Count) players in $SourceDir ($($players.Name -join ', '))." -ForegroundColor Yellow
    Write-Host "         Packaging the newest. Build with -Clean to avoid mixing them." -ForegroundColor Yellow
}
$exeName = $players[0].Name

# --------------------------------------------------------------- the version
if (-not $Version) {
    $settings = Join-Path $projectPath "ProjectSettings\ProjectSettings.asset"
    $line = Select-String -Path $settings -Pattern '^\s*bundleVersion:\s*(.+)$' |
        Select-Object -First 1
    if (-not $line) { Fail "Could not read bundleVersion from $settings — pass -Version." }
    $Version = $line.Matches[0].Groups[1].Value.Trim()
}
# VersionInfoVersion in the setup .exe's resources must be numeric and 4-part.
$numeric = ($Version -replace '[^0-9.].*$', '').Trim('.')
if (-not $numeric) { $numeric = "0" }
$parts = @($numeric -split '\.') + @('0', '0', '0', '0')
$versionInfo = ($parts[0..3]) -join '.'

# ------------------------------------------------------------------ the tool
if (-not $IsccPath) {
    # ProgramW6432 as well as ProgramFiles: a 32-bit parent (GNU Make for
    # Windows) has WOW64 point ProgramFiles at the (x86) folder for everything
    # it launches.
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:ProgramW6432\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -notmatch '^\\' }
    $IsccPath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $IsccPath) {
        $IsccPath = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
    }
}
if (-not $IsccPath -or -not (Test-Path $IsccPath)) {
    Fail @"
Inno Setup 6 not found. Install it, then re-run:
    winget install --id JRSoftware.InnoSetup
Or pass -IsccPath "C:\path\to\ISCC.exe".
"@
}

# --------------------------------------------------------------- the artwork
if (-not (Test-Path (Join-Path $installerDir "assets\iron-meridian.ico"))) {
    Write-Host "Installer artwork missing — generating it..."
    & python (Join-Path $projectPath "scripts\generate_installer_art.py")
    if ($LASTEXITCODE -ne 0) { Fail "generate_installer_art.py failed (needs Pillow)." }
}

# ------------------------------------------------------------------ warnings
$tokenInBuild = Get-ChildItem -Path $SourceDir -Filter "cesium-token.txt" -Recurse -File -ErrorAction SilentlyContinue
if ($IncludeToken) {
    if ($tokenInBuild) {
        Write-Host "WARNING: bundling the Cesium ion token — anyone you give this installer to gets the token." -ForegroundColor Yellow
    } else {
        Write-Host "WARNING: -IncludeToken given but no cesium-token.txt in the build; the map will stay empty." -ForegroundColor Yellow
    }
} elseif ($tokenInBuild) {
    Write-Host "Excluding the Cesium ion token from the package (pass -IncludeToken to ship it)." -ForegroundColor Yellow
}

$outPath = if ([System.IO.Path]::IsPathRooted($OutputDir)) { $OutputDir } else { Join-Path $projectPath $OutputDir }
New-Item -ItemType Directory -Force -Path $outPath | Out-Null
$outPath = (Resolve-Path $outPath).Path.TrimEnd('\')

# ------------------------------------------------------------------- compile
Write-Host ""
Write-Host "Packaging Iron Meridian $Version"
Write-Host "  player : $SourceDir ($exeName)"
Write-Host "  output : $outPath"
Write-Host ""

$isccArgs = @(
    "/DAppVersion=$Version",
    "/DVersionInfo=$versionInfo",
    "/DSourceDir=$SourceDir",
    "/DExeName=$exeName",
    "/DOutDir=$outPath"
)
if ($IncludeToken) { $isccArgs += "/DIncludeToken=1" }
if ($SignToolCommand) {
    $isccArgs += "/DSignToolName=ironmeridian"
    $isccArgs += "/Sironmeridian=$SignToolCommand"
}
$isccArgs += (Join-Path $installerDir "iron-meridian.iss")

& $IsccPath @isccArgs
if ($LASTEXITCODE -ne 0) { Fail "Inno Setup failed (exit $LASTEXITCODE)." }

# -------------------------------------------------------------------- report
$setup = Join-Path $outPath "IronMeridian-$Version-Setup.exe"
if (-not (Test-Path $setup)) { Fail "Inno Setup reported success but $setup is missing." }

$size = "{0:N1} MB" -f ((Get-Item $setup).Length / 1MB)
$sha = (Get-FileHash $setup -Algorithm SHA256).Hash
Write-Host ""
Write-Host "Done: $setup" -ForegroundColor Green
Write-Host "  size   : $size"
Write-Host "  sha256 : $sha"
