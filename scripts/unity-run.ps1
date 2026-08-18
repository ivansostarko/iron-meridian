# Iron Meridian — run one Unity editor method in batch mode
#
# The Tools menu is the interactive face of a handful of static methods; this
# is the other face, so a task runner can call them without the editor open.
#
# Usage:
#   .\scripts\unity-run.ps1 -Method IronMeridian.EditorTools.ProjectBootstrap.SetupProject
#   .\scripts\unity-run.ps1 -PrintPath          # just resolve the editor
#
# It is also where the editor is *found*, for every other script in here.
param(
    # Fully-qualified static method, e.g. Namespace.Class.Method.
    [string]$Method,
    # Defaults to the newest Unity 6000.x under the Hub.
    [string]$UnityPath,
    # Defaults to Builds\<method>.log.
    [string]$LogFile,
    # Print the resolved editor path and exit — how other scripts ask.
    [switch]$PrintPath
)

$ErrorActionPreference = "Stop"
$projectPath = (Resolve-Path "$PSScriptRoot\..").Path

if (-not $UnityPath) {
    # ProgramW6432 first, and never ProgramFiles alone: under a 32-bit parent
    # (GNU Make for Windows is one) WOW64 rewrites ProgramFiles to the (x86)
    # folder, and every child inherits it. PowerShell 7 quietly corrects that,
    # Windows PowerShell does not — so the same command found Unity or didn't
    # depending on which shell ran it.
    $roots = @($env:ProgramW6432, ${env:ProgramFiles}, "C:\Program Files") |
        Where-Object { $_ } | Select-Object -Unique |
        ForEach-Object { Join-Path $_ "Unity\Hub\Editor" }

    # Wherever the Hub was told to put editors instead.
    $hubConfig = Join-Path $env:APPDATA "UnityHub\secondaryInstallPath.json"
    if (Test-Path $hubConfig) {
        $custom = (Get-Content $hubConfig -Raw).Trim().Trim('"')
        if ($custom) { $roots += $custom }
    }

    $installed = @($roots |
        Where-Object { Test-Path $_ } |
        ForEach-Object { Get-ChildItem $_ -Directory } |
        Where-Object { $_.Name -like "6000.*" } |
        Where-Object { Test-Path (Join-Path $_.FullName "Editor\Unity.exe") })

    # The editor the project was last opened with. Opening a Unity project in a
    # newer editor upgrades its format, and there is no going back — so an exact
    # match wins over a newer one every time, and picking something else is
    # something the user gets told about rather than discovering afterwards.
    $wanted = $null
    $versionFile = Join-Path $projectPath "ProjectSettings\ProjectVersion.txt"
    if (Test-Path $versionFile) {
        $line = Select-String -Path $versionFile -Pattern '^m_EditorVersion:\s*(.+)$' | Select-Object -First 1
        if ($line) { $wanted = $line.Matches[0].Groups[1].Value.Trim() }
    }

    $match = $installed | Where-Object { $_.Name -eq $wanted } | Select-Object -First 1
    if ($match) {
        $UnityPath = Join-Path $match.FullName "Editor\Unity.exe"
    } elseif ($installed.Count -gt 0) {
        # Version order, not string order: 6000.10 must beat 6000.5.
        $newest = $installed |
            Sort-Object { try { [version]($_.Name -replace '[^0-9.].*$', '') } catch { [version]"0.0" } } -Descending |
            Select-Object -First 1
        if ($wanted) {
            Write-Host "WARNING: this project was made with Unity $wanted, which is not installed." -ForegroundColor Yellow
            Write-Host "         Using $($newest.Name) instead — it will UPGRADE the project format, irreversibly." -ForegroundColor Yellow
            Write-Host "         Install $wanted from Unity Hub, or pass -UnityPath to choose deliberately." -ForegroundColor Yellow
        }
        $UnityPath = Join-Path $newest.FullName "Editor\Unity.exe"
    }
}
if (-not $UnityPath -or -not (Test-Path $UnityPath)) {
    # -PrintPath is a question, not a command: answer it with the exit code and
    # let the caller decide how loudly to complain.
    if (-not $PrintPath) {
        Write-Host "ERROR: Unity 6000.0 LTS not found. Pass -UnityPath ""...\Editor\Unity.exe""." -ForegroundColor Red
    }
    exit 1
}

if ($PrintPath) { Write-Output $UnityPath; exit 0 }
if (-not $Method) { Write-Host "ERROR: -Method is required." -ForegroundColor Red; exit 1 }

# Batch mode cannot open a project the editor already holds, and the error it
# gives for that is a lock-file message four screens down a log nobody reads.
if (Get-Process -Name "Unity" -ErrorAction SilentlyContinue) {
    Write-Host "ERROR: Unity is running. Close the project first — batch mode cannot share it." -ForegroundColor Red
    exit 1
}

if (-not $LogFile) {
    New-Item -ItemType Directory -Force -Path (Join-Path $projectPath "Builds") | Out-Null
    $LogFile = Join-Path $projectPath "Builds\$($Method.Split('.')[-1]).log"
}

Write-Host "Unity: $Method"
Write-Host "  log: $LogFile"

& $UnityPath `
    -batchmode -nographics -quit `
    -projectPath $projectPath `
    -executeMethod $Method `
    -logFile $LogFile

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "FAILED (exit $LASTEXITCODE) — tail of $LogFile" -ForegroundColor Red
    if (Test-Path $LogFile) { Get-Content $LogFile -Tail 30 | ForEach-Object { Write-Host "  $_" } }
    exit $LASTEXITCODE
}

Write-Host "Done." -ForegroundColor Green
