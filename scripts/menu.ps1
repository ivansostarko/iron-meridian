# Iron Meridian — the task menu
#
#   .\scripts\menu.ps1              pick a job from a list
#   .\scripts\menu.ps1 -List        print the list and exit (what `make help` shows)
#   .\scripts\menu.ps1 -Run build   run one job by name (what every make target does)
#
# This file is the register of routine jobs: the Makefile's targets and the
# interactive menu are both driven from the $Jobs table below, so adding a job
# here adds it to both. Keep the Makefile target names identical to the keys.
param(
    [string]$Run,
    [switch]$List
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path "$PSScriptRoot\..").Path

function Invoke-Script($name, $arguments = @()) {
    & (Join-Path $PSScriptRoot $name) @arguments
    if ($LASTEXITCODE -ne 0) { throw "$name failed (exit $LASTEXITCODE)" }
}

function Invoke-Python($name) {
    & python (Join-Path $PSScriptRoot $name)
    if ($LASTEXITCODE -ne 0) { throw "$name failed (needs Pillow: pip install pillow)" }
}

function Invoke-Unity($method) { Invoke-Script "unity-run.ps1" @("-Method", $method) }

# --------------------------------------------------------------------- the jobs
$Jobs = @(
    @{ Group = "Build and ship"; Key = "setup"; Text = "Generate the scenes and build settings (Unity, project closed)"
       Do = { Invoke-Unity "IronMeridian.EditorTools.ProjectBootstrap.SetupProject" } }
    @{ Group = "Build and ship"; Key = "build"; Text = "Build the Windows player into Builds\Windows"
       Do = { Invoke-Script "build-windows.ps1" } }
    @{ Group = "Build and ship"; Key = "installer"; Text = "Build the player and package it as a setup .exe"
       Do = { Invoke-Script "build-windows.ps1" @("-Clean", "-Installer") } }
    @{ Group = "Build and ship"; Key = "package"; Text = "Package the player already in Builds\Windows"
       Do = { Invoke-Script "build-installer.ps1" } }
    @{ Group = "Build and ship"; Key = "run"; Text = "Launch the built player"
       Do = {
            $exe = Get-ChildItem (Join-Path $root "Builds\Windows") -Filter *.exe -ErrorAction SilentlyContinue |
                Where-Object { Test-Path (Join-Path $_.DirectoryName "$($_.BaseName)_Data") } |
                Sort-Object LastWriteTime -Descending | Select-Object -First 1
            if (-not $exe) { throw "No player in Builds\Windows — run 'build' first." }
            Write-Host "Starting $($exe.Name)"
            Start-Process $exe.FullName
       } }

    @{ Group = "Data and artwork"; Key = "units"; Text = "Regenerate units.json from generate_units.py"
       Do = { Invoke-Python "generate_units.py" } }
    @{ Group = "Data and artwork"; Key = "icons"; Text = "Regenerate the APP-6 unit icons"
       Do = { Invoke-Python "generate_icons.py" } }
    @{ Group = "Data and artwork"; Key = "stat-icons"; Text = "Regenerate the unit info panel's stat glyphs"
       Do = { Invoke-Python "generate_stat_icons.py" } }
    @{ Group = "Data and artwork"; Key = "units-doc"; Text = "Rewrite the tables in docs/04-UNITS.md from units.json"
       Do = { Invoke-Python "generate_units_doc.py" } }
    @{ Group = "Data and artwork"; Key = "installer-art"; Text = "Regenerate the installer icon and wizard bitmaps"
       Do = { Invoke-Python "generate_installer_art.py" } }
    @{ Group = "Data and artwork"; Key = "data"; Text = "All of the above, in the order they depend on each other"
       Do = {
            # units.json first: the icons are drawn per unit id and the doc is
            # rewritten from the file, so either one run early is stale.
            Invoke-Python "generate_units.py"; Invoke-Python "generate_icons.py"
            Invoke-Python "generate_stat_icons.py"; Invoke-Python "generate_units_doc.py"
       } }

    @{ Group = "Unity tools"; Key = "models"; Text = "Install Unit Models (regenerates Resources/Models prefabs)"
       Do = { Invoke-Unity "IronMeridian.EditorTools.ModelInstaller.Install" } }
    @{ Group = "Unity tools"; Key = "vfx"; Text = "Install VFX Prefabs into Resources/VFX"
       Do = { Invoke-Unity "IronMeridian.EditorTools.VfxInstaller.Install" } }
    @{ Group = "Unity tools"; Key = "packages"; Text = "Import bundled .unitypackage files (Built-In pipeline)"
       Do = { Invoke-Unity "IronMeridian.EditorTools.PackageImporter.ImportBundled" } }

    @{ Group = "Steam"; Key = "steam-check"; Text = "Release preflight: app id, icon, version, build, licences"
       Do = { Invoke-Script "steam-check.ps1" } }
    @{ Group = "Steam"; Key = "steam-appid"; Text = "Write steam_appid.txt beside the player, to test Steam locally"
       Do = {
            $integration = Join-Path $root "Assets\Scripts\Core\SteamIntegration.cs"
            $match = Select-String -Path $integration -Pattern 'public const uint AppId\s*=\s*(\d+)' | Select-Object -First 1
            if (-not $match) { throw "Could not read AppId from SteamIntegration.cs" }
            $appId = $match.Matches[0].Groups[1].Value
            $build = Join-Path $root "Builds\Windows"
            if (-not (Test-Path $build)) { throw "No player in Builds\Windows — run 'build' first." }
            # No trailing newline: Steam's own docs are explicit that the file
            # is the id and nothing else.
            [System.IO.File]::WriteAllText((Join-Path $build "steam_appid.txt"), $appId)
            Write-Host "Wrote steam_appid.txt ($appId) into Builds\Windows."
            if ($appId -eq "480") {
                Write-Host "That is Valve's Spacewar test app — fine for trying the overlay, not for release." -ForegroundColor Yellow
            }
            Write-Host "It is excluded from the Steam depot on purpose; see docs/36-STEAM.md."
       } }

    @{ Group = "Project"; Key = "packages-audit"; Text = "Report which Unity packages nothing uses (‑Apply strips them)"
       Do = { Invoke-Script "packages-reset.ps1" } }
    @{ Group = "Project"; Key = "check"; Text = "Compile every C# file with Roslyn, without opening Unity"
       Do = { Invoke-Script "compile-check.ps1" } }
    @{ Group = "Project"; Key = "doctor"; Text = "Check the toolchain: Unity, Python, Pillow, Inno Setup, token"
       Do = { Invoke-Doctor } }
    @{ Group = "Project"; Key = "logs"; Text = "Tail the last Unity setup and build logs"
       Do = {
            $logs = @(Get-ChildItem (Join-Path $root "Builds") -Filter *.log -ErrorAction SilentlyContinue |
                Sort-Object LastWriteTime -Descending)
            if ($logs.Count -eq 0) { Write-Host "No logs in Builds\ yet."; return }
            foreach ($log in $logs) {
                Write-Host ""
                Write-Host "--- $($log.Name)  ($($log.LastWriteTime))" -ForegroundColor Cyan
                Get-Content $log.FullName -Tail 20 | ForEach-Object { Write-Host "  $_" }
            }
       } }
    @{ Group = "Project"; Key = "clean"; Text = "Delete the packaged installers and the build logs"
       Do = {
            foreach ($p in @("Builds\Installer", "Builds\*.log")) {
                $full = Join-Path $root $p
                if (Test-Path $full) { Remove-Item -Recurse -Force $full; Write-Host "removed $p" }
            }
            Write-Host "The player in Builds\Windows was kept — 'distclean' removes that too."
       } }
    @{ Group = "Project"; Key = "distclean"; Text = "Delete everything under Builds\, player included"
       Do = {
            $builds = Join-Path $root "Builds"
            if (-not (Test-Path $builds)) { Write-Host "Nothing to remove."; return }
            $size = "{0:N0} MB" -f ((Get-ChildItem $builds -Recurse -File | Measure-Object Length -Sum).Sum / 1MB)
            # A player is twenty minutes of Unity; never throw one away silently.
            if (-not $env:FORCE) {
                $answer = Read-Host "Delete Builds\ ($size)? Rebuilding the player takes ~20 min [y/N]"
                if ($answer -notmatch '^[Yy]') { Write-Host "Kept."; return }
            }
            Remove-Item -Recurse -Force $builds
            Write-Host "removed Builds\ ($size)"
       } }
)

# ------------------------------------------------------------------- the doctor
function Invoke-Doctor {
    $script:problems = 0
    function Report($name, $ok, $detail, $fix) {
        if ($ok) {
            Write-Host ("  {0,-14} " -f $name) -NoNewline
            Write-Host "ok" -ForegroundColor Green -NoNewline
            Write-Host "   $detail"
        } else {
            Write-Host ("  {0,-14} " -f $name) -NoNewline
            Write-Host "missing" -ForegroundColor Yellow -NoNewline
            Write-Host "  $fix"
            $script:problems++
        }
    }

    Write-Host ""
    Write-Host "Toolchain" -ForegroundColor Cyan

    $unity = & (Join-Path $PSScriptRoot "unity-run.ps1") -PrintPath 2>$null
    Report "Unity" ($LASTEXITCODE -eq 0) $unity "install Unity 6000.0 LTS with Windows Build Support"

    $py = (Get-Command python -ErrorAction SilentlyContinue)
    Report "Python" ($null -ne $py) $py.Source "needed only to regenerate data and artwork"

    if ($py) {
        & python -c "import PIL" 2>$null
        Report "Pillow" ($LASTEXITCODE -eq 0) "" "pip install pillow"
    }

    $iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
              "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
              "$env:ProgramW6432\Inno Setup 6\ISCC.exe") |
        Where-Object { $_ -notmatch '^\\' } |
        Where-Object { Test-Path $_ } | Select-Object -First 1
    Report "Inno Setup" ($null -ne $iscc) $iscc "winget install --id JRSoftware.InnoSetup  (installer only)"

    Write-Host ""
    Write-Host "Project" -ForegroundColor Cyan

    # Presence only. The token is a secret — never print it (golden rule 1).
    $token = Join-Path $root "Assets\StreamingAssets\cesium-token.txt"
    $hasToken = (Test-Path $token) -and ((Get-Content $token -Raw).Trim() -notmatch '^(PASTE_YOUR|$)')
    Report "Cesium token" $hasToken "configured (not shown)" "paste one into Assets\StreamingAssets\cesium-token.txt — docs/02-CESIUM.md"

    $scenes = Join-Path $root "Assets\Scenes"
    Report "Scenes" (Test-Path $scenes) "$((Get-ChildItem $scenes -Filter *.unity -EA SilentlyContinue).Count) generated" "run 'setup'"

    $player = Get-ChildItem (Join-Path $root "Builds\Windows") -Filter *.exe -EA SilentlyContinue |
        Where-Object { Test-Path (Join-Path $_.DirectoryName "$($_.BaseName)_Data") } | Select-Object -First 1
    Report "Player" ($null -ne $player) $player.Name "run 'build'"

    Write-Host ""
    if ($script:problems -eq 0) { Write-Host "Everything needed is in place." -ForegroundColor Green }
    else { Write-Host "$script:problems thing(s) to sort out — see above." -ForegroundColor Yellow }
}

# ------------------------------------------------------------------ presentation
function Show-List {
    Write-Host ""
    Write-Host "  IRON MERIDIAN" -ForegroundColor Cyan -NoNewline
    Write-Host "   real-terrain operational wargame"
    Write-Host ""
    $n = 0
    foreach ($group in ($Jobs.Group | Select-Object -Unique)) {
        Write-Host "  $group" -ForegroundColor DarkCyan
        foreach ($job in ($Jobs | Where-Object Group -eq $group)) {
            $n++
            Write-Host ("   {0,2}. " -f $n) -ForegroundColor DarkGray -NoNewline
            Write-Host ("{0,-14}" -f $job.Key) -ForegroundColor White -NoNewline
            Write-Host " $($job.Text)" -ForegroundColor Gray
        }
        Write-Host ""
    }
    Write-Host "  make <name>" -ForegroundColor DarkGray -NoNewline
    Write-Host "  ·  " -ForegroundColor DarkGray -NoNewline
    Write-Host ".\scripts\menu.ps1" -ForegroundColor DarkGray -NoNewline
    Write-Host " to pick from this list  ·  the .ps1 files take options" -ForegroundColor DarkGray
    Write-Host ""
}

function Invoke-Job($job) {
    Write-Host ""
    Write-Host "==> $($job.Key)" -ForegroundColor Cyan
    & $job.Do
}

# -------------------------------------------------------------------- dispatch
if ($List) { Show-List; exit 0 }

if ($Run) {
    $job = $Jobs | Where-Object Key -eq $Run | Select-Object -First 1
    if (-not $job) {
        Write-Host "ERROR: no job called '$Run'. Known: $(($Jobs.Key) -join ', ')" -ForegroundColor Red
        exit 1
    }
    try { Invoke-Job $job } catch { Write-Host "ERROR: $_" -ForegroundColor Red; exit 1 }
    exit 0
}

# No arguments: pick one, then come back for another.
while ($true) {
    Show-List
    Write-Host "   q. quit" -ForegroundColor DarkGray
    Write-Host ""
    $choice = Read-Host "  Choose"
    if ([string]::IsNullOrWhiteSpace($choice) -or $choice -match '^[Qq]') { break }

    $job = $null
    if ($choice -match '^\d+$' -and [int]$choice -ge 1 -and [int]$choice -le $Jobs.Count) {
        $job = $Jobs[[int]$choice - 1]
    } else {
        $job = $Jobs | Where-Object Key -eq $choice.Trim() | Select-Object -First 1
    }
    if (-not $job) { Write-Host "  Not a choice." -ForegroundColor Yellow; continue }

    try { Invoke-Job $job } catch { Write-Host "ERROR: $_" -ForegroundColor Red }
    Write-Host ""
    Read-Host "  Press Enter for the menu" | Out-Null
}
