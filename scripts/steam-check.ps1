# Iron Meridian — Steam release preflight
#
# Checks the mechanical half of "is this ready to put on Steam". It cannot tell
# you whether the game is good, whether your asset licences permit a commercial
# release, or whether Cesium ion will bill you into the ground — those are in
# docs/36-STEAM.md and only you can close them.
#
# Usage:
#   .\scripts\steam-check.ps1
#
# Exit code 1 if anything is a FAIL. Warnings do not fail the run.
param([string]$SourceDir = "Builds\Windows")

$ErrorActionPreference = "Stop"
$root = (Resolve-Path "$PSScriptRoot\..").Path
$script:fails = 0
$script:warns = 0

function Section($title) {
    Write-Host ""
    Write-Host $title -ForegroundColor Cyan
}

function Result($state, $name, $detail) {
    $colour = switch ($state) { "pass" { "Green" } "warn" { "Yellow" } default { "Red" } }
    $label = switch ($state) { "pass" { "ok  " } "warn" { "warn" } default { "FAIL" } }
    if ($state -eq "fail") { $script:fails++ } elseif ($state -eq "warn") { $script:warns++ }
    Write-Host "  " -NoNewline
    Write-Host $label -ForegroundColor $colour -NoNewline
    Write-Host ("  {0,-22} " -f $name) -NoNewline
    Write-Host $detail -ForegroundColor Gray
}

# ------------------------------------------------------------- the integration
Section "Steamworks"

$defines = ""
$settingsFile = Join-Path $root "ProjectSettings\ProjectSettings.asset"
if (Test-Path $settingsFile) {
    $raw = Get-Content $settingsFile -Raw
    $m = [regex]::Match($raw, '(?ms)^\s*scriptingDefineSymbols:\s*(\{\}|.*?)(?=^\s*\w+:)')
    if ($m.Success) { $defines = $m.Groups[1].Value }
}
if ($defines -match "IRONMERIDIAN_STEAM") {
    Result pass "define" "IRONMERIDIAN_STEAM is set — the Steam code is compiled in"
} else {
    Result warn "define" "IRONMERIDIAN_STEAM not set: the game builds without Steam support (docs/36-STEAM.md section 3)"
}

$sdk = @("Assets\Plugins\Steamworks.NET", "Assets\com.rlabrecque.steamworks.net", "Packages\com.rlabrecque.steamworks.net") |
    Where-Object { Test-Path (Join-Path $root $_) } | Select-Object -First 1
$sdkInManifest = (Get-Content (Join-Path $root "Packages\manifest.json") -Raw) -match "steamworks"
if ($sdk -or $sdkInManifest) {
    Result pass "Steamworks.NET" $(if ($sdk) { $sdk } else { "referenced from Packages/manifest.json" })
} else {
    Result warn "Steamworks.NET" "not installed — SteamIntegration compiles to no-ops"
}

$integration = Join-Path $root "Assets\Scripts\Core\SteamIntegration.cs"
if (Test-Path $integration) {
    $appIdLine = Select-String -Path $integration -Pattern 'public const uint AppId\s*=\s*(\d+)' | Select-Object -First 1
    $codeAppId = if ($appIdLine) { $appIdLine.Matches[0].Groups[1].Value } else { $null }
    if ($codeAppId -eq "480") {
        Result fail "app id (code)" "still 480 (Valve's Spacewar test app) — put your own in SteamIntegration.cs"
    } elseif ($codeAppId) {
        Result pass "app id (code)" $codeAppId
    } else {
        Result warn "app id (code)" "could not read AppId from SteamIntegration.cs"
    }
}

$appVdf = Join-Path $root "steam\app_build.vdf"
if (Test-Path $appVdf) {
    $vdfAppId = [regex]::Match((Get-Content $appVdf -Raw), '"appid"\s+"([^"]*)"').Groups[1].Value
    if ($vdfAppId -like "*{{*") {
        Result fail "app id (vdf)" "steam\app_build.vdf still has the {{APPID}} placeholder"
    } elseif ($vdfAppId -ne $codeAppId -and $codeAppId) {
        Result fail "app id (vdf)" "$vdfAppId does not match the code's $codeAppId — they must be the same app"
    } else {
        Result pass "app id (vdf)" $vdfAppId
    }
}

$steamCmd = @((Join-Path $root "steam\steamcmd\steamcmd.exe"),
              "$env:ProgramW6432\steamcmd\steamcmd.exe") |
    Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $steamCmd) { $steamCmd = (Get-Command steamcmd.exe -ErrorAction SilentlyContinue).Source }
if ($steamCmd) { Result pass "steamcmd" $steamCmd }
else { Result warn "steamcmd" "not found — needed only to upload (steam\steamcmd\)" }

# -------------------------------------------------------------- the presentation
Section "Presentation"

$iconDir = Join-Path $root "Assets\AppIcon"
$icons = @(Get-ChildItem $iconDir -Filter "icon-*.png" -ErrorAction SilentlyContinue)
if ($icons.Count -ge 6) { Result pass "app icon" "$($icons.Count) sizes in Assets\AppIcon" }
else { Result fail "app icon" "run: make installer-art  (the build would ship Unity's default icon)" }

if ($raw -match 'm_ShowUnitySplashScreen:\s*1') {
    Result warn "Unity splash" "shown at startup — removable only on a Unity licence that allows it (docs/36-STEAM.md section 1)"
} else {
    Result pass "Unity splash" "disabled"
}

$gameConfig = Join-Path $root "Assets\Scripts\Core\GameConfig.cs"
$verLine = Select-String -Path $gameConfig -Pattern 'Version\s*=\s*"([^"]+)"' | Select-Object -First 1
$version = if ($verLine) { $verLine.Matches[0].Groups[1].Value } else { "" }
$bundleLine = Select-String -Path $settingsFile -Pattern '^\s*bundleVersion:\s*(.+)$' | Select-Object -First 1
$bundle = if ($bundleLine) { $bundleLine.Matches[0].Groups[1].Value.Trim() } else { "" }
if ($version -match 'dev|alpha|wip') {
    Result warn "version" "GameConfig.Version is '$version' — buyers see this"
} elseif ($version -and $version -eq $bundle) {
    Result pass "version" $version
} else {
    Result warn "version" "GameConfig.Version '$version' vs bundleVersion '$bundle' — run: make setup"
}

# --------------------------------------------------------------------- the build
Section "Build"

if (-not [System.IO.Path]::IsPathRooted($SourceDir)) { $SourceDir = Join-Path $root $SourceDir }
if (-not (Test-Path $SourceDir)) {
    Result fail "player" "nothing at $SourceDir — run: make build"
} else {
    $player = Get-ChildItem $SourceDir -Filter *.exe |
        Where-Object { Test-Path (Join-Path $SourceDir "$($_.BaseName)_Data") } | Select-Object -First 1
    if ($player) { Result pass "player" $player.Name } else { Result fail "player" "no .exe beside a matching *_Data" }

    $junk = @(Get-ChildItem $SourceDir -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like "*_DoNotShip*" -or $_.Extension -eq ".pdb" })
    if ($junk.Count -eq 0) { Result pass "no debug files" "no *_DoNotShip or .pdb in the build" }
    else { Result warn "no debug files" "$($junk.Count) present — the depot excludes them, but rebuild clean to be sure" }

    $token = Get-ChildItem $SourceDir -Filter "cesium-token.txt" -Recurse -File -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($token) {
        Result warn "Cesium token" "present in the build — steam-upload.ps1 requires an explicit -Token choice"
    } else {
        Result warn "Cesium token" "absent — the shipped game draws no terrain unless players supply one"
    }
}

# ------------------------------------------------------------------- the licences
Section "Third-party content (only you can clear these)"

$packs = @(Get-ChildItem (Join-Path $root "Assets") -Directory |
    Where-Object { $_.Name -notin @("Scripts", "Editor", "Scenes", "Resources", "StreamingAssets", "AppIcon", "CesiumSettings") })
Result warn "asset packs" "$($packs.Count) third-party folders under Assets\ — each needs a licence that permits commercial release"
foreach ($p in $packs) { Write-Host "         $($p.Name)" -ForegroundColor DarkGray }

# ---------------------------------------------------------------------- verdict
Write-Host ""
if ($script:fails -gt 0) {
    Write-Host "$($script:fails) blocking problem(s), $($script:warns) to review. See docs/36-STEAM.md." -ForegroundColor Red
    exit 1
}
Write-Host "No blocking problems. $($script:warns) item(s) to review — see docs/36-STEAM.md." -ForegroundColor Green
