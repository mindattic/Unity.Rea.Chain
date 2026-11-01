<#
.SYNOPSIS
  Regenerates Assets/Scenes/Main.unity and the project settings, then builds the Windows player
  (Builds/Windows/Unity.Rea.Chain.exe), each step in Unity batch mode (tools/unity.ps1 does the work;
  the Editor must be closed). Mirrors the README's "Build and test" PowerShell block; see
  Unity.SpaceGod's and Unity.GridGame's tools/build.ps1.
.EXAMPLE
  powershell -File tools/build.ps1
  build.bat
#>
$ErrorActionPreference = 'Stop'

Write-Host '  Regenerating Main scene...' -ForegroundColor Cyan
& "$PSScriptRoot/unity.ps1" -Method MindAttic.Chain.EditorTools.SceneBuilder.BuildMainScene -Log build-scene
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host '  Applying project settings...' -ForegroundColor Cyan
& "$PSScriptRoot/unity.ps1" -Method MindAttic.Chain.EditorTools.ProjectSettingsBuilder.Apply -Log build-settings
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host '  Building Windows player...' -ForegroundColor Cyan
& "$PSScriptRoot/unity.ps1" -Method MindAttic.Chain.EditorTools.PlayerBuilder.BuildWindows -Log build-player
exit $LASTEXITCODE
