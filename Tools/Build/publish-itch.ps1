<#
.SYNOPSIS
    Pushes a build made by build.ps1 to itch.io with butler.

.DESCRIPTION
    Pushes Builds/<version>/windows-client to allyn-david/riverfront-next:windows, tagged with
    --userversion <version>. butler only uploads what changed since the last push to that channel.

    Credentials come from butler itself: either run `butler login` once on this PC, or set the
    BUTLER_API_KEY environment variable (an API key from https://itch.io/user/settings/api-keys).
    Never commit the key.

.PARAMETER Version
    Build folder under Builds/ to push. Defaults to the most recently written one.

.PARAMETER IncludeServer
    Also push the dedicated server to the windows-server channel. Off by default: the server is for
    people hosting LAN/direct-IP games, so decide whether you want it on the public page first.

.PARAMETER DryRun
    Print what would be pushed without uploading.

.EXAMPLE
    ./Tools/Build/publish-itch.ps1 -DryRun
.EXAMPLE
    ./Tools/Build/publish-itch.ps1 -Version 0.1.0+abc1234
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$Target = 'allyn-david/riverfront-next',
    [string]$ClientChannel = 'windows',
    [string]$ServerChannel = 'windows-server',
    [switch]$IncludeServer,
    [switch]$AllowDirty,
    [switch]$DryRun,
    [string]$ButlerPath
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$buildsRoot = Join-Path $ProjectRoot 'Builds'

# --- Pick the build ---
if (-not $Version) {
    $latest = Get-ChildItem $buildsRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { Test-Path (Join-Path $_.FullName 'build-info.txt') } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $latest) { throw "No builds under $buildsRoot. Run Tools/Build/build.ps1 first." }
    $Version = $latest.Name
}
$buildDir = Join-Path $buildsRoot $Version
if (-not (Test-Path (Join-Path $buildDir 'build-info.txt'))) { throw "No finished build at $buildDir." }
if ($Version -like '*-dirty' -and -not $AllowDirty) {
    throw "Build $Version was made from uncommitted changes. Commit and rebuild, or pass -AllowDirty."
}

$pushes = @(@{ Dir = Join-Path $buildDir 'windows-client'; Channel = $ClientChannel })
if ($IncludeServer) { $pushes += @{ Dir = Join-Path $buildDir 'windows-server'; Channel = $ServerChannel } }
foreach ($p in $pushes) {
    if (-not (Test-Path (Join-Path $p.Dir '*.exe'))) { throw "No .exe in $($p.Dir)." }
    if (Get-ChildItem $p.Dir -Directory | Where-Object Name -match 'DoNotShip|DontShip') {
        throw "$($p.Dir) still contains debug-symbol folders; rebuild with build.ps1."
    }
}

# --- Find butler ---
if (-not $ButlerPath) {
    $cmd = Get-Command butler -ErrorAction SilentlyContinue
    if ($cmd) { $ButlerPath = $cmd.Source }
    elseif (Test-Path 'C:\butler\butler.exe') { $ButlerPath = 'C:\butler\butler.exe' }
    else { throw 'butler not found. Install it from https://itchio.itch.io/butler (or the itch app) and add it to PATH.' }
}

foreach ($p in $pushes) {
    $dest = "$($Target):$($p.Channel)"
    Write-Host "Push $($p.Dir) -> $dest (userversion $Version)"
    if ($DryRun) { continue }
    & $ButlerPath push $p.Dir $dest --userversion $Version
    if ($LASTEXITCODE -ne 0) { throw "butler push to $dest failed (exit $LASTEXITCODE). Run 'butler login' or set BUTLER_API_KEY if it was an auth error." }
}

if ($DryRun) {
    Write-Host 'Dry run: nothing uploaded.' -ForegroundColor Yellow
} else {
    Write-Host "Done. Check progress with: butler status $($Target):$ClientChannel" -ForegroundColor Green
}
