# Iron Meridian — WebGL batch build
#
# Usage:
#   .\scripts\build-web.ps1
#   .\scripts\build-web.ps1 -Development         # full exceptions with stack traces
#   .\scripts\build-web.ps1 -Uncompressed        # for a host that cannot send Content-Encoding
#   .\scripts\build-web.ps1 -Serve               # build, then serve it locally and open a browser
#   .\scripts\build-web.ps1 -ServeOnly           # serve whatever is already in Builds\Web
#
# A WebGL build CANNOT be opened with file:// — the browser refuses to fetch the
# .wasm and the StreamingAssets data across that origin. It has to be served over
# http, which is what -Serve is for.
#
# See docs/41-WEB.md.
param(
    # Defaults to the newest Unity 6000.x found under the Hub's editor folder.
    [string]$UnityPath,
    [string]$OutputDir = "Builds\Web",
    # Development build: full exception support with stack traces. Much bigger.
    [switch]$Development,
    # No Brotli. Bigger download, but needs nothing configured on the server.
    [switch]$Uncompressed,
    # Empty the output folder first.
    [switch]$Clean,
    # Serve the result on http://localhost:<Port> and open a browser.
    [switch]$Serve,
    # Skip the build and just serve what is already there.
    [switch]$ServeOnly,
    [int]$Port = 8080
)

$ErrorActionPreference = "Stop"
$projectPath = (Resolve-Path "$PSScriptRoot\..").Path
$outPath = if ([System.IO.Path]::IsPathRooted($OutputDir)) { $OutputDir } else { Join-Path $projectPath $OutputDir }

# ------------------------------------------------------------------- serving
function Start-WebServer($root, $port) {
    if (-not (Test-Path (Join-Path $root "index.html"))) {
        Write-Host "ERROR: no index.html in $root - build first." -ForegroundColor Red
        exit 1
    }

    # Python is already a project prerequisite for the data generators, and its
    # http.server sends the Content-Encoding headers a Brotli build needs when
    # given the map below. A bespoke .NET HttpListener would be a second web
    # server to maintain for no gain.
    $py = (Get-Command python -ErrorAction SilentlyContinue)
    if (-not $py) {
        Write-Host "ERROR: python not found - needed to serve the build locally." -ForegroundColor Red
        Write-Host "  Or serve $root with any static server that sends Content-Encoding." -ForegroundColor Yellow
        exit 1
    }

    $serverPy = Join-Path $PSScriptRoot "serve-web.py"
    Write-Host ""
    Write-Host "Serving $root on http://localhost:$port  (Ctrl+C to stop)" -ForegroundColor Cyan
    Start-Process "http://localhost:$port"
    & python $serverPy $root $port
}

if ($ServeOnly) { Start-WebServer $outPath $Port; exit 0 }

# ------------------------------------------------------------------ the editor
# unity-run.ps1 owns editor discovery for every script in here.
if (-not $UnityPath) {
    $UnityPath = & (Join-Path $PSScriptRoot "unity-run.ps1") -PrintPath
}
if (-not $UnityPath -or -not (Test-Path $UnityPath)) {
    Write-Host "ERROR: Unity 6000.0 LTS not found. Pass -UnityPath ""...\Editor\Unity.exe""." -ForegroundColor Red
    exit 1
}

# WebGL Build Support is an optional editor module. Without it the build fails
# some minutes in with a message about a missing target, so say so now.
$editorDir = Split-Path $UnityPath
$playbackEngine = Join-Path $editorDir "Data\PlaybackEngines\WebGLSupport"
if (-not (Test-Path $playbackEngine)) {
    Write-Host "ERROR: WebGL Build Support is not installed for this editor." -ForegroundColor Red
    Write-Host "  Unity Hub -> Installs -> the 6000.0 editor -> Add modules -> WebGL Build Support." -ForegroundColor Yellow
    exit 1
}

if ($Clean -and (Test-Path $outPath)) {
    Write-Host "Cleaning $outPath"
    Remove-Item -Recurse -Force $outPath
}
New-Item -ItemType Directory -Force -Path $outPath | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $projectPath "Builds") | Out-Null

Write-Host "Building Iron Meridian for the web -> $outPath"
Write-Host "  editor: $UnityPath"
if ($Uncompressed) { Write-Host "  uncompressed - larger download, no server config needed" -ForegroundColor Yellow }

# Setup first, same as every other build: it writes the scene list AND
# StreamingAssets\Maps\index.json, which the WebGL preload reads to know which
# scenarios to fetch. See docs\41-WEB.md.
& $UnityPath `
    -batchmode -nographics -quit `
    -projectPath $projectPath `
    -executeMethod IronMeridian.EditorTools.ProjectBootstrap.SetupProject `
    -logFile (Join-Path $projectPath "Builds\setup-web.log")
if ($LASTEXITCODE -ne 0) { throw "Scene setup failed - see Builds\setup-web.log" }

$buildArgs = @(
    "-batchmode", "-nographics", "-quit",
    "-projectPath", $projectPath,
    "-buildTarget", "WebGL",
    "-executeMethod", "IronMeridian.EditorTools.WebBuild.BuildFromCommandLine",
    "-ironmeridian-output", $outPath,
    "-logFile", (Join-Path $projectPath "Builds\build-web.log")
)
if ($Development) { $buildArgs += "-ironmeridian-development" }
if ($Uncompressed) { $buildArgs += "-ironmeridian-uncompressed" }

& $UnityPath @buildArgs
if ($LASTEXITCODE -ne 0) { throw "Build failed - see Builds\build-web.log" }

if (-not (Test-Path (Join-Path $outPath "index.html"))) {
    throw "Build reported success but there is no index.html in $outPath."
}

$sizeMb = [math]::Round(((Get-ChildItem $outPath -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Host "Done: $outPath ($sizeMb MB on disk)"

# ------------------------------------------------------------------ the token
# The same warning the installer exists to make unnecessary: StreamingAssets is
# served as plain files, so cesium-token.txt is one fetch away from anyone who
# loads the page. docs\41-WEB.md section 6.
$token = Join-Path $outPath "StreamingAssets\cesium-token.txt"
if (Test-Path $token) {
    $content = (Get-Content $token -Raw).Trim()
    if ($content -and $content -notmatch '^PASTE_YOUR') {
        Write-Host ""
        Write-Host "WARNING: this build carries your Cesium ion token." -ForegroundColor Yellow
        Write-Host "  StreamingAssets is served as plain files - anyone who loads the page can read it." -ForegroundColor Yellow
        Write-Host "  Use a token restricted to asset read on the tilesets, and be ready to revoke it." -ForegroundColor Yellow
        Write-Host "  docs/41-WEB.md section 6." -ForegroundColor Yellow
    }
}

if ($Serve) { Start-WebServer $outPath $Port }
