# Iron Meridian — reset Packages/manifest.json to what the project actually uses
#
# Three rounds of speculative installs took the project from 22 to 90 resolved
# packages, none of them referenced by a line of game code. The visible cost is
# an editor full of menus and overlays for features that do not exist, Package
# Manager churn in the console, and a bigger build. docs/38-PACKAGES.md has the
# per-package reasoning.
#
# Usage:
#   .\scripts\packages-reset.ps1            # show what would change, touch nothing
#   .\scripts\packages-reset.ps1 -Apply     # do it (Unity must be closed)
#
# See docs/38-PACKAGES.md.
param(
    # Without this the script only reports. Nothing is written.
    [switch]$Apply,
    # Extra package ids to keep beyond the defaults below.
    [string[]]$AlsoKeep = @()
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path "$PSScriptRoot\..").Path
$manifestPath = Join-Path $root "Packages\manifest.json"

# Everything the project references, plus the one tool with a job waiting for it.
# Engine modules (com.unity.modules.*) are always kept - they are the engine.
$keep = @(
    "com.cesium.unity"        # the whole point - docs/02-CESIUM.md
    "com.unity.ai.navigation" # NavMesh, used by ground movement
    "com.unity.ugui"          # every screen, built at runtime by UIFactory
    "com.unity.recorder"      # the Steam trailer and screenshots - docs/36-STEAM.md section 6
) + $AlsoKeep

function Fail($m) { Write-Host "ERROR: $m" -ForegroundColor Red; exit 1 }

if (-not (Test-Path $manifestPath)) { Fail "No manifest at $manifestPath" }

$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$deps = $manifest.dependencies

$removing = @()
$keeping  = @()
foreach ($name in ($deps.PSObject.Properties.Name | Sort-Object)) {
    if ($name -like "com.unity.modules.*" -or $keep -contains $name) { $keeping += $name }
    else { $removing += $name }
}

Write-Host ""
Write-Host "Keeping $($keeping.Count) (including $(@($keeping | Where-Object { $_ -like '*modules*' }).Count) engine modules):" -ForegroundColor Green
foreach ($n in $keeping) { if ($n -notlike "com.unity.modules.*") { Write-Host "    $n" -ForegroundColor Green } }
Write-Host ""
Write-Host "Removing $($removing.Count):" -ForegroundColor Yellow
foreach ($n in $removing) { Write-Host "    $n" -ForegroundColor Yellow }
Write-Host ""

if (-not $Apply) {
    Write-Host "Nothing written. Re-run with -Apply (and Unity closed) to make the change."
    exit 0
}

# The editor holds the resolved package set in memory and rewrites this file
# from it, so an edit made while it is open silently reverts within seconds.
# docs/38-PACKAGES.md section 4.
if (Get-Process -Name "Unity" -ErrorAction SilentlyContinue) {
    Fail @"
Unity is running. It rewrites Packages\manifest.json from its own state, so this
edit would be reverted within seconds.

Close Unity, run this again, then reopen the project - it will re-resolve and
drop the removed packages.
"@
}

$backup = "$manifestPath.bak"
Copy-Item $manifestPath $backup -Force

# Rebuild dependencies in the original order, minus the removals, so the diff
# stays readable rather than a wholesale reordering.
$clean = [ordered]@{}
foreach ($name in $deps.PSObject.Properties.Name) {
    if ($keeping -contains $name) { $clean[$name] = $deps.$name }
}
$out = [ordered]@{}
if ($manifest.PSObject.Properties.Name -contains "scopedRegistries") {
    $out["scopedRegistries"] = $manifest.scopedRegistries
}
$out["dependencies"] = $clean

$json = $out | ConvertTo-Json -Depth 10
Set-Content -Path $manifestPath -Value $json -Encoding UTF8

Write-Host "Wrote $manifestPath" -ForegroundColor Green
Write-Host "  backup: $backup"
Write-Host ""
Write-Host "Open the project in Unity; it will re-resolve and packages-lock.json will shrink."
Write-Host "To undo: copy the .bak back over it before opening Unity."
