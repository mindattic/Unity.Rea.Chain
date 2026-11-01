<#
.SYNOPSIS
  Headless verification gate for Unity.Rea.Chain: the engine-free Core rules (dotnet test, via
  run-core-tests.ps1), the EditMode component tests, and the PlayMode suite -- today just
  MainSceneSmokeTests (Assets/Unity.Rea.Chain/Tests/PlayMode) -- each run in Unity batch mode
  (tools/unity.ps1; the Editor must be closed). Mirrors the README's "Build and test" PowerShell
  block; see Unity.SpaceGod's and Unity.GridGame's tools/test.ps1.
.PARAMETER SkipCore
  Skip Tests/Core.Tests.
.PARAMETER SkipEditMode
  Skip the Unity EditMode suite.
.PARAMETER SkipPlayMode
  Skip the Unity PlayMode suite (MainSceneSmokeTests).
.EXAMPLE
  powershell -File tools/test.ps1
  test.bat
  powershell -File tools/test.ps1 -SkipEditMode -SkipPlayMode   # Core.Tests only
#>
param([switch]$SkipCore, [switch]$SkipEditMode, [switch]$SkipPlayMode)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# Unity's -runTests exits 0 even when it matched zero tests (a wrong -testPlatform, an excluded
# assembly, or a renamed test all look like success otherwise), which would let "make the PlayMode
# MainSceneSmokeTests part of that run" silently stop being true. Confirm the results file actually
# names tests -- and, for PlayMode, that MainSceneSmokeTests is one of them -- not just that Unity
# exited 0. A stale file from an earlier run is removed first so a crashed run can't be read as this
# one's results.
function Confirm-TestResults([string]$Path, [string]$MustContain) {
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Path was not written; the Unity test run produced no results."
    }
    [xml]$results = Get-Content -LiteralPath $Path -Raw
    $run = $results.'test-run'
    if (-not $run -or [int]$run.total -le 0) {
        throw "$Path reports zero tests; -runTests matched nothing."
    }
    if ($MustContain -and $results.SelectNodes("//test-case[contains(@fullname, '$MustContain')]").Count -eq 0) {
        throw "$Path has no test-case whose fullname contains '$MustContain'."
    }
    Write-Host "    $Path`: $($run.passed)/$($run.total) passed" -ForegroundColor Gray
}

if (-not $SkipCore) {
    Write-Host '  Core.Tests (dotnet test)...' -ForegroundColor Cyan
    & (Join-Path $root 'run-core-tests.ps1')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if (-not $SkipEditMode) {
    Write-Host '  EditMode tests (Unity batch mode)...' -ForegroundColor Cyan
    $editResults = Join-Path $root 'TestResults-EditMode.xml'
    Remove-Item -LiteralPath $editResults -ErrorAction SilentlyContinue
    & "$PSScriptRoot/unity.ps1" -Log test-editmode -Extra "-runTests -testPlatform EditMode -assemblyNames Unity.Rea.Chain.EditMode.Tests -testResults $editResults"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Confirm-TestResults -Path $editResults
}

if (-not $SkipPlayMode) {
    # The game's two PlayMode assemblies: MainSceneSmokeTests and the splash, and the online tests on the
    # Unity.Multiplayer kit (U.CHN-T26). -assemblyNames is needed since NGO is listed under "testables" (the kit's
    # test fixtures need it), which would otherwise run NGO's own suites too; a new game test assembly joins the list.
    # Confirm-TestResults below is what actually proves it ran, not just this comment.
    Write-Host '  PlayMode tests (Unity batch mode, includes MainSceneSmokeTests)...' -ForegroundColor Cyan
    $playResults = Join-Path $root 'TestResults-PlayMode.xml'
    Remove-Item -LiteralPath $playResults -ErrorAction SilentlyContinue
    & "$PSScriptRoot/unity.ps1" -Log test-playmode -Extra "-runTests -testPlatform PlayMode -assemblyNames Unity.Rea.Chain.PlayMode.Tests;Unity.Rea.Chain.Online.Tests -testResults $playResults"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Confirm-TestResults -Path $playResults -MustContain 'MainSceneSmokeTests'
}

exit 0
