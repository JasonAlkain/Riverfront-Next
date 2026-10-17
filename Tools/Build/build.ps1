<#
.SYNOPSIS
    Builds the Windows client and dedicated server in batchmode.

.DESCRIPTION
    Runs Unity headless against this project and calls Game.Conquest.Build.ReleaseBuild.
    Output goes to Builds/<version>/ (gitignored):

        Builds/<version>/windows-client/Riverfront Next.exe   <- what goes to itch.io
        Builds/<version>/windows-server/Riverfront Next.exe   <- dedicated server
        Builds/<version>/symbols/                             <- debug folders that must not ship
        Builds/<version>/build-info.txt
        Builds/<version>/zips/*.zip                           <- with -Zip

    The Unity project must not be open in the Editor while this runs (Unity locks the project).

.PARAMETER Version
    Version string baked into Application.version. Defaults to "<bundleVersion>+<git short sha>",
    with "-dirty" appended when the working tree has uncommitted changes.
    Client and server only connect when their versions match.

.PARAMETER Targets
    client, server, or client,server (default).

.EXAMPLE
    ./Tools/Build/build.ps1
.EXAMPLE
    ./Tools/Build/build.ps1 -Version 0.2.0 -Targets client -Zip
#>
[CmdletBinding()]
param(
    [string]$Version,
    [ValidateSet('client', 'server', 'client,server')]
    [string]$Targets = 'client,server',
    [string]$UnityPath = $env:UNITY_PATH,
    [switch]$Development,
    [switch]$Zip
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

# --- Locate the Unity editor that matches ProjectVersion.txt ---
$editorVersion = (Select-String -Path (Join-Path $ProjectRoot 'ProjectSettings\ProjectVersion.txt') -Pattern '^m_EditorVersion:\s*(\S+)').Matches[0].Groups[1].Value
if (-not $UnityPath) {
    $UnityPath = Join-Path $env:ProgramFiles "Unity\Hub\Editor\$editorVersion\Editor\Unity.exe"
}
if (-not (Test-Path $UnityPath)) {
    throw "Unity $editorVersion not found at '$UnityPath'. Install it through Unity Hub or pass -UnityPath / set UNITY_PATH."
}

# --- Version ---
if (-not $Version) {
    $bundleVersion = (Select-String -Path (Join-Path $ProjectRoot 'ProjectSettings\ProjectSettings.asset') -Pattern '^\s*bundleVersion:\s*(\S+)').Matches[0].Groups[1].Value
    $sha = (git -C $ProjectRoot rev-parse --short HEAD).Trim()
    $Version = "$bundleVersion+$sha"
    if (git -C $ProjectRoot status --porcelain) { $Version += '-dirty' }
}
if ($Version -notmatch '^[0-9A-Za-z.+_-]+$') {
    throw "Version '$Version' may only contain letters, digits and . + _ -"
}

$outputRoot = Join-Path $ProjectRoot "Builds\$Version"
$logDir = Join-Path $ProjectRoot 'Logs'
New-Item -ItemType Directory -Force $logDir | Out-Null
$logFile = Join-Path $logDir "build-$Version.log"

Write-Host "Unity    : $UnityPath"
Write-Host "Version  : $Version"
Write-Host "Targets  : $Targets"
Write-Host "Output   : $outputRoot"
Write-Host "Log      : $logFile"

$unityArgs = @(
    '-batchmode', '-quit', '-nographics',
    '-projectPath', "`"$ProjectRoot`"",
    '-buildTarget', 'Win64',
    '-executeMethod', 'Game.Conquest.Build.ReleaseBuild.BuildFromCommandLine',
    '-buildVersion', $Version,
    '-buildOutput', "`"$outputRoot`"",
    '-buildTargets', $Targets,
    '-logFile', "`"$logFile`""
)
if ($Development) { $unityArgs += '-developmentBuild' }

$sw = [Diagnostics.Stopwatch]::StartNew()
$proc = Start-Process -FilePath $UnityPath -ArgumentList $unityArgs -Wait -PassThru -NoNewWindow
$sw.Stop()

if ($proc.ExitCode -ne 0) {
    Write-Host "Build FAILED (exit $($proc.ExitCode)) after $([int]$sw.Elapsed.TotalMinutes) min. Last log lines:" -ForegroundColor Red
    Get-Content $logFile -Tail 40 -ErrorAction SilentlyContinue
    exit $proc.ExitCode
}

if ($Zip) {
    $zipDir = Join-Path $outputRoot 'zips'
    New-Item -ItemType Directory -Force $zipDir | Out-Null
    foreach ($folder in 'windows-client', 'windows-server') {
        $src = Join-Path $outputRoot $folder
        if (Test-Path $src) {
            $zipPath = Join-Path $zipDir "RiverfrontNext-$Version-$folder.zip"
            Compress-Archive -Path "$src\*" -DestinationPath $zipPath -Force
            Write-Host "Zipped   : $zipPath"
        }
    }
}

Write-Host "Build OK in $([int]$sw.Elapsed.TotalMinutes) min -> $outputRoot" -ForegroundColor Green
