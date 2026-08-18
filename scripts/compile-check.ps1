# Iron Meridian — compile the runtime C# without opening Unity
#
# There is no test suite, and a full editor round-trip is minutes. This runs
# Roslyn straight over Assets/**/*.cs against Unity's own reference assemblies,
# so a typo is caught in seconds instead of on the next Play.
#
# Usage:
#   .\scripts\compile-check.ps1
#   .\scripts\compile-check.ps1 -Define IRONMERIDIAN_STEAM
#
# Limits, so the result is not over-trusted:
#   * It compiles against Library/ScriptAssemblies, so Unity must have imported
#     the project at least once.
#   * Runtime and editor scripts land in one assembly rather than the two Unity
#     makes. That is fine for finding errors and would not be fine for running.
#   * It proves the code compiles. It proves nothing about whether it works.
param(
    # Extra scripting defines, e.g. the Steam integration's IRONMERIDIAN_STEAM.
    [string[]]$Define = @(),
    # Leave out Assets/**/Editor/** — faster, and all the game code is still
    # covered. On by default because editor tools are where Setup Project lives
    # and a typo there is not visible until someone runs it.
    [switch]$SkipEditor,
    [string]$UnityPath
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path "$PSScriptRoot\..").Path

function Fail($message) { Write-Host "ERROR: $message" -ForegroundColor Red; exit 1 }

# ------------------------------------------------------------------ the tools
if (-not $UnityPath) { $UnityPath = & (Join-Path $PSScriptRoot "unity-run.ps1") -PrintPath }
if (-not $UnityPath -or -not (Test-Path $UnityPath)) { Fail "Unity not found — see .\scripts\unity-run.ps1." }
$data = Join-Path (Split-Path $UnityPath) "Data"

$sdkRoot = Join-Path $env:ProgramW6432 "dotnet\sdk"
if (-not (Test-Path $sdkRoot)) { $sdkRoot = Join-Path ${env:ProgramFiles} "dotnet\sdk" }
$csc = Get-ChildItem $sdkRoot -Directory -ErrorAction SilentlyContinue |
    Sort-Object { try { [version]($_.Name -replace '-.*$', '') } catch { [version]"0.0" } } -Descending |
    ForEach-Object { Join-Path $_.FullName "Roslyn\bincore\csc.dll" } |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) { Fail "No .NET SDK with Roslyn found. Install the .NET SDK: winget install --id Microsoft.DotNet.SDK.9" }

$assemblies = Join-Path $root "Library\ScriptAssemblies"
if (-not (Test-Path $assemblies)) {
    Fail "Library\ScriptAssemblies is missing — open the project in Unity once so it imports."
}

# ------------------------------------------------------------- the references
$refs = @(Join-Path $data "NetStandard\ref\2.1.0\netstandard.dll")
$refs += Get-ChildItem (Join-Path $data "Managed\UnityEngine") -Filter "UnityEngine*.dll" |
    ForEach-Object { $_.FullName }
# Everything Unity compiled for the project except the game's own assemblies —
# those are the output, not an input.
$refs += Get-ChildItem $assemblies -Filter *.dll |
    Where-Object { $_.Name -notlike "Assembly-CSharp*" } |
    ForEach-Object { $_.FullName }
if (-not $SkipEditor) { $refs += Join-Path $data "Managed\UnityEditor.dll" }
$refs = $refs | Where-Object { Test-Path $_ }

# ---------------------------------------------------------------- the sources
$sources = @(Get-ChildItem (Join-Path $root "Assets") -Filter *.cs -Recurse -File |
    Where-Object { -not ($SkipEditor -and $_.FullName -match '\\Editor\\') } |
    ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) { Fail "No sources under Assets." }

# Every path is quoted: this project's own directory has a space in it, and an
# unquoted line in a response file is silently split into two bogus arguments.
$out = Join-Path ([System.IO.Path]::GetTempPath()) "iron-meridian-compile-check.dll"
$rsp = Join-Path ([System.IO.Path]::GetTempPath()) "iron-meridian-compile-check.rsp"
$lines = @('-target:library', '-nostdlib+', '-nologo', '-langversion:9.0', "-out:`"$out`"")
foreach ($d in $Define) { $lines += "-define:$d" }
foreach ($r in $refs) { $lines += "-r:`"$r`"" }
foreach ($s in $sources) { $lines += "`"$s`"" }
Set-Content -Path $rsp -Value $lines -Encoding UTF8

$editorVersion = Split-Path (Split-Path (Split-Path $data)) -Leaf
Write-Host "Compiling $($sources.Count) files against Unity $editorVersion"
if ($Define.Count) { Write-Host "  defines: $($Define -join ', ')" }

$output = & dotnet $csc "@$rsp" 2>&1
$errors = $output | Select-String -Pattern ': error CS'

if ($errors) {
    Write-Host ""
    $errors | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    Write-Host ""
    Write-Host "$($errors.Count) compile error(s)." -ForegroundColor Red
    exit 1
}

Write-Host "No compile errors." -ForegroundColor Green
