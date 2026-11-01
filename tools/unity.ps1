<#
.SYNOPSIS
  Runs Unity.Rea.Chain in Unity batch mode and surfaces compile/test errors -- the shared helper
  build.ps1, test.ps1 and run.ps1 all call (same pattern as Unity.Hare, Unity.Eve and
  Unity.Cursory's tools/unity.ps1, and Unity.GridGame's own tools/unity.ps1).
.EXAMPLE
  powershell -File tools/unity.ps1 -Method MindAttic.Chain.EditorTools.PlayerBuilder.BuildWindows -Log build-player
  powershell -File tools/unity.ps1 -Log test-editmode -Extra "-runTests -testPlatform EditMode -testResults TestResults-EditMode.xml"
.NOTES
  Unity batch mode needs the Editor closed on this project. This script refuses to run if it is open.
  Every run's full Unity log is captured to Logs/<Log>.log (the "capture of a run" UNI-T39 asks for).
#>
param(
    [string]$Method,
    [string]$Log = "unity",
    [switch]$Graphics,
    [string]$Extra = ""
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$version = (Get-Content "$root/ProjectSettings/ProjectVersion.txt" | Select-String 'm_EditorVersion:').ToString().Split(':')[1].Trim()
$unity = "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe"
if (-not (Test-Path $unity)) { throw "Unity $version not found at $unity" }

if (Test-Path "$root/Temp/UnityLockfile") {
    $locked = $false
    try { [IO.File]::Open("$root/Temp/UnityLockfile", 'Open', 'ReadWrite', 'None').Close() } catch { $locked = $true }
    if ($locked) { Write-Error "Unity.Rea.Chain is open in the Unity Editor. Close it (File > Quit) and re-run."; exit 3 }
}

New-Item -ItemType Directory -Force "$root/Logs" | Out-Null
$logFile = "$root/Logs/$Log.log"
# Start-Process joins these with bare spaces, so paths are quoted here or a space in one would split it.
$argList = @("-batchmode", "-projectPath", "`"$root`"", "-logFile", "`"$logFile`"")
if (-not $Graphics) { $argList += "-nographics" }
if ($Method) { $argList += @("-executeMethod", $Method) }
if ($Extra) { $argList += $Extra.Split(' ') }
# A -runTests run quits on its own once the suite finishes; an -executeMethod run needs -quit.
if ($Extra -notmatch 'runTests') { $argList += "-quit" }

$p = Start-Process -FilePath $unity -ArgumentList $argList -Wait -PassThru -NoNewWindow
Select-String -Path $logFile -Pattern 'error CS|\[Unity\.Rea\.Chain\]|\[Build\]|Shader error|Compilation failed' -ErrorAction SilentlyContinue |
    Select-Object -First 80 | ForEach-Object { Write-Host $_.Line }
Write-Host "exit=$($p.ExitCode)  log=$logFile"
exit $p.ExitCode
