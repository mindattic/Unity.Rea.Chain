<#
.SYNOPSIS
    Launches a playable Unity.Rea.Chain for development, muted.
.DESCRIPTION
    Starts Builds/Windows/Unity.Rea.Chain.exe (building it with tools/build.ps1 first when it is
    missing or -Build is given) with MINDATTIC_CLI_MUTED=1 and -muted, so a CLI or agent launching
    the game never plays sound. The mute is Unity.Shared's session mute: it lasts for this run only
    and never changes the player's saved settings.json (Profiles.Current.Settings, U.CHN-T74). Pass
    -Sound to launch with audio. Any other arguments go to the player (for example
    -screen-fullscreen 0). The player's log is Logs/play.log. Same shape as Unity.Rea.QuickDuel's
    play.ps1.
.PARAMETER Build
    Rebuild the player with tools/build.ps1 before launching.
.PARAMETER Sound
    Launch with audio (no MINDATTIC_CLI_MUTED, no -muted).
.PARAMETER Capture
    U.CHN-T62's scripted run: launch the capture-enabled player (Builds/Capture/*.exe, built with
    tools/unity.ps1 -Method MindAttic.Capture.Unity.Editor.CaptureBuild.BuildWindows) with -demo and
    -capture <mode>, wait for it and return its exit code (non-zero when a shot or the clip failed).
    Still muted unless -Sound. Example: -Capture 'screen 1000' or -Capture video.
.PARAMETER Wait
    Wait for the player to exit and return its exit code.
.EXAMPLE
    powershell -File tools/play.ps1
    play.bat
    powershell -File tools/play.ps1 -Sound
#>
param(
    [switch]$Build,
    [switch]$Sound,
    [switch]$Wait,
    [string]$Capture,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$PlayerArgs
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot   # tools\ -> Unity.Rea.Chain\
$exe = Join-Path $root 'Builds/Windows/Unity.Rea.Chain.exe'
if ($Capture) {
    $found = Get-ChildItem (Join-Path $root 'Builds/Capture') -Filter *.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notmatch 'Crash' } | Select-Object -First 1
    if (-not $found) { throw 'No capture player in Builds/Capture; build it first (see -Capture).' }
    $exe = $found.FullName
    $Wait = $true
}
$log = Join-Path $root 'Logs/play.log'

if (-not $Capture -and ($Build -or -not (Test-Path $exe))) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'build.ps1')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
New-Item -ItemType Directory -Force (Split-Path $log) | Out-Null

$arguments = @('-logFile', "`"$log`"")
if (-not $Sound) { $arguments += '-muted' }
if ($Capture) { $arguments += @('-demo', '-capture') + $Capture.Split(' ') }
if ($PlayerArgs) { $arguments += $PlayerArgs }

# The child inherits this process's environment; set the mute only around the launch.
$previous = $env:MINDATTIC_CLI_MUTED
if ($Sound) { Remove-Item Env:MINDATTIC_CLI_MUTED -ErrorAction SilentlyContinue } else { $env:MINDATTIC_CLI_MUTED = '1' }
try {
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru
} finally {
    if ($null -eq $previous) { Remove-Item Env:MINDATTIC_CLI_MUTED -ErrorAction SilentlyContinue } else { $env:MINDATTIC_CLI_MUTED = $previous }
}

Write-Host ("Launched Unity.Rea.Chain (pid {0}, {1}); log {2}" -f $process.Id, $(if ($Sound) { 'with sound' } else { 'muted' }), $log)
if ($Wait) {
    $null = $process.Handle   # cache the handle so ExitCode is readable after the exit
    $process.WaitForExit()
    exit $process.ExitCode
}
exit 0
