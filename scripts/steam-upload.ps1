# Iron Meridian — upload a built player to a Steam depot
#
# Resolves the templates in steam\ into *.local.vdf with absolute paths, then
# hands them to steamcmd. Nothing goes live unless you name a branch.
#
# Usage:
#   .\scripts\steam-upload.ps1 -Token Exclude -Preview
#   .\scripts\steam-upload.ps1 -Token Exclude -User yourlogin
#   .\scripts\steam-upload.ps1 -Token Include -User yourlogin -Branch beta
#
# See docs/36-STEAM.md.
param(
    # Whether the Cesium ion token is uploaded with the game. Deliberately has
    # no default: on Steam, "Include" hands your metered token to every buyer
    # and "Exclude" ships a game that draws no ground. Both are real choices
    # and neither should happen because a script guessed. docs/36-STEAM.md §2.
    [Parameter(Mandatory = $true)]
    [ValidateSet("Include", "Exclude")]
    [string]$Token,

    # Steam account with upload rights on the app.
    [string]$User,

    # Build the manifest and report, upload nothing. No -User needed.
    [switch]$Preview,

    # Branch to set live, e.g. "beta". Empty uploads without releasing, which
    # is what you want almost every time — you can set it live from the
    # partner site after looking at it.
    [string]$Branch = "",

    [string]$SourceDir = "Builds\Windows",
    [string]$Desc,
    [string]$SteamCmd
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path "$PSScriptRoot\..").Path
$steamDir = Join-Path $root "steam"

function Fail($message) { Write-Host "ERROR: $message" -ForegroundColor Red; exit 1 }

# ------------------------------------------------------------------- the ids
# Committed in the templates; not secrets.
$appTemplate = Join-Path $steamDir "app_build.vdf"
$depotTemplate = Join-Path $steamDir "depot_windows.vdf"
foreach ($t in @($appTemplate, $depotTemplate)) {
    if (-not (Test-Path $t)) { Fail "Missing template: $t" }
}

function Placeholder($text, $name) {
    $m = [regex]::Match($text, "`"$name`"\s+`"([^`"]*)`"")
    if ($m.Success) { return $m.Groups[1].Value }
    return $null
}

$appText = Get-Content $appTemplate -Raw
$appId = Placeholder $appText "appid"
$depotId = Placeholder $appText "depotid"
if (-not $depotId) { $depotId = Placeholder (Get-Content $depotTemplate -Raw) "DepotID" }

if ($appId -like "*{{*" -or -not $appId) {
    Fail @"
steam\app_build.vdf still has the {{APPID}} placeholder.
Put your app id and depot id from the Steamworks partner site into
steam\app_build.vdf and steam\depot_windows.vdf, then run this again.
"@
}
if ($appId -eq "480") {
    Fail "App id 480 is Spacewar, Valve's test app. You cannot upload to it — use your own."
}

# ---------------------------------------------------------------- the player
if (-not [System.IO.Path]::IsPathRooted($SourceDir)) { $SourceDir = Join-Path $root $SourceDir }
if (-not (Test-Path $SourceDir)) { Fail "No player build at $SourceDir. Run: make build" }
$SourceDir = (Resolve-Path $SourceDir).Path.TrimEnd('\')

$players = @(Get-ChildItem $SourceDir -Filter *.exe |
    Where-Object { Test-Path (Join-Path $SourceDir "$($_.BaseName)_Data") })
if ($players.Count -eq 0) { Fail "No Unity player in $SourceDir (an .exe beside a matching *_Data)." }

# ----------------------------------------------------------------- the token
$tokenFile = Get-ChildItem $SourceDir -Filter "cesium-token.txt" -Recurse -File -ErrorAction SilentlyContinue |
    Select-Object -First 1
if ($Token -eq "Include") {
    if (-not $tokenFile) { Fail "-Token Include, but there is no cesium-token.txt in the build." }
    Write-Host "The Cesium ion token WILL be uploaded. Every buyer gets it, and every buyer's" -ForegroundColor Yellow
    Write-Host "tile request is billed to it. Confirm you have read docs/36-STEAM.md section 2." -ForegroundColor Yellow
} elseif ($tokenFile) {
    Write-Host "Excluding the Cesium ion token. The shipped game will draw no terrain until the" -ForegroundColor Yellow
    Write-Host "player supplies one — make sure that is what you intend." -ForegroundColor Yellow
}

# --------------------------------------------------------------- the tooling
if (-not $SteamCmd) {
    $SteamCmd = @(
        (Join-Path $steamDir "steamcmd\steamcmd.exe"),
        "$env:ProgramW6432\steamcmd\steamcmd.exe",
        "${env:ProgramFiles}\steamcmd\steamcmd.exe"
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $SteamCmd) { $SteamCmd = (Get-Command steamcmd.exe -ErrorAction SilentlyContinue).Source }
}
if (-not $SteamCmd) {
    Fail @"
steamcmd not found. Download the Steamworks SDK (partner site) or the standalone
steamcmd, and put steamcmd.exe in steam\steamcmd\ (git-ignored), or pass -SteamCmd.
"@
}
if (-not $Preview -and -not $User) { Fail "-User is required unless -Preview is given." }

# ------------------------------------------------------------- the local vdf
$buildOutput = Join-Path $root "steam\build_output"
New-Item -ItemType Directory -Force -Path $buildOutput | Out-Null

if (-not $Desc) {
    $settings = Join-Path $root "ProjectSettings\ProjectSettings.asset"
    $line = Select-String -Path $settings -Pattern '^\s*bundleVersion:\s*(.+)$' | Select-Object -First 1
    $version = if ($line) { $line.Matches[0].Groups[1].Value.Trim() } else { "unversioned" }
    $stamp = Get-Date -Format "yyyy-MM-dd HH:mm"
    $Desc = "Iron Meridian $version ($stamp)"
}

$depotLocal = Join-Path $steamDir "depot_windows.local.vdf"
$depotText = (Get-Content $depotTemplate -Raw).
    Replace("{{DEPOTID}}", $depotId).
    Replace("{{CONTENTROOT}}", $SourceDir)
if ($Token -eq "Include") {
    # Drop the exclusion line rather than editing the committed template.
    $depotText = $depotText -replace '(?m)^\s*"FileExclusion"\s+"cesium-token\.txt".*$', "`t// cesium-token.txt included by -Token Include"
}
Set-Content -Path $depotLocal -Value $depotText -Encoding UTF8

$appLocal = Join-Path $steamDir "app_build.local.vdf"
$appOut = $appText.
    Replace("{{APPID}}", $appId).
    Replace("{{DEPOTID}}", $depotId).
    Replace("{{DESC}}", $Desc).
    Replace("{{BUILDOUTPUT}}", $buildOutput).
    Replace("{{CONTENTROOT}}", $SourceDir).
    Replace("{{SETLIVE}}", $Branch).
    Replace("{{PREVIEW}}", $(if ($Preview) { "1" } else { "0" })).
    Replace("{{DEPOTFILE}}", $depotLocal)
Set-Content -Path $appLocal -Value $appOut -Encoding UTF8

# ------------------------------------------------------------------- the run
Write-Host ""
Write-Host "Uploading to Steam"
Write-Host "  app     : $appId  depot $depotId"
Write-Host "  player  : $SourceDir ($($players[0].Name))"
Write-Host "  desc    : $Desc"
Write-Host "  token   : $Token"
Write-Host "  branch  : $(if ($Branch) { $Branch } else { '(none — upload only, not live)' })"
if ($Preview) { Write-Host "  preview : nothing will be uploaded" -ForegroundColor Cyan }
Write-Host ""

$steamArgs = @()
if ($Preview) { $steamArgs += @("+login", "anonymous") } else { $steamArgs += @("+login", $User) }
$steamArgs += @("+run_app_build", $appLocal, "+quit")

& $SteamCmd @steamArgs
if ($LASTEXITCODE -ne 0) {
    Fail "steamcmd exited $LASTEXITCODE. Its log is under $buildOutput."
}

Write-Host ""
Write-Host "Done. Build logs: $buildOutput" -ForegroundColor Green
if (-not $Branch) {
    Write-Host "Not live: set the build live from the partner site's Builds page when you are ready."
}
