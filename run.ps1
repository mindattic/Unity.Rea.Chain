#Requires -Version 5.1
<#
.SYNOPSIS
    Rebuilds Unity.Rea.Chain (tools\build.ps1: regenerates the scene and project settings, then the
    Windows player) only when Assets/Packages/ProjectSettings changed since the last deploy, and
    publishes a standalone copy to C:\Apps\Unity.Rea.Chain\ -- same process Unity.SpaceGod and
    Unity.GridGame use to always have a fresh, independent deployed copy instead of a possibly-stale
    one.

.DESCRIPTION
    1. Pulls latest (skipped if the worktree has uncommitted changes, or the pull fails).
    2. Skips the rebuild if nothing under Assets/Packages/ProjectSettings changed since the last
       deploy at this commit (unless -Force).
    3. Stops any running Unity.Rea.Chain.exe process (dev copy or deployed copy).
    4. Clears Builds\Windows -- the build OUTPUT only, never Library\ -- so the build below is
       never a stale incremental copy.
    5. Runs tools\build.ps1 (Unity batch mode; requires the Editor closed) -> Builds\Windows\Unity.Rea.Chain.exe.
       Its own Unity run captures Logs\build-scene.log, Logs\build-settings.log and Logs\build-player.log.
    6. Copies Builds\Windows\* -> C:\Apps\Unity.Rea.Chain\, refreshes the Desktop shortcut, and
       optionally launches it.

.PARAMETER Launch
    After publishing, immediately run the deployed Unity.Rea.Chain.exe.

.PARAMETER Force
    Rebuild and republish even when no player source changed since the last deploy (skips only
    the up-to-date check; Library\ and user settings are untouched).

.NOTES
    This script never prompts, so agents and /deploy can run it unattended. The repo-root
    run.bat (the Desktop shortcut's target) is the interactive entry point: it asks
    1) Deploy & Run (this script with -Force -Launch) or 2) Run (starts the copy already in
    C:\Apps\Unity.Rea.Chain without calling this script, deploying first if none is there).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\run.ps1
    powershell -ExecutionPolicy Bypass -File tools\run.ps1 -Launch
    powershell -ExecutionPolicy Bypass -File tools\run.ps1 -Force -Launch
#>
param([switch]$Launch, [switch]$Force)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoDir   = Split-Path $PSScriptRoot   # tools\ -> Unity.Rea.Chain\
$out       = 'C:\Apps\Unity.Rea.Chain'
$exeName   = 'Unity.Rea.Chain.exe'
$buildDir  = Join-Path $repoDir 'Builds\Windows'
$runBat    = Join-Path $repoDir 'run.bat'
$marker    = Join-Path $out '.deploy-commit'

# ── Pull latest, skip the rebuild entirely if nothing changed ──────────────
# Untracked files don't block a pull; changes to tracked files (staged or not) skip it, as does a
# pull that fails: a desktop shortcut running this must still deploy the commit already checked out.
$dirty = git -C $repoDir status --porcelain | Where-Object { $_ -notmatch '^\?\? ' }
if ($dirty) {
    Write-Host "  $repoDir has uncommitted changes - not pulling; deploying the current commit." -ForegroundColor DarkYellow
} else {
    git -C $repoDir pull --ff-only
    if ($LASTEXITCODE -ne 0) { Write-Host "  git pull --ff-only failed in $repoDir - deploying the current commit." -ForegroundColor DarkYellow }
}
$head = (git -C $repoDir rev-parse HEAD).Trim()

# Points at run.bat (not the exe directly), so double-clicking it offers 1) Deploy & Run
# (pull latest, rebuild, publish, launch) or 2) Run (launch the installed copy).
# GetFolderPath, not $HOME\Desktop: OneDrive may have moved the Desktop folder.
function Set-Shortcut([string]$exe) {
    $shortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Unity.Rea.Chain.lnk'
    $link = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcut)
    $link.TargetPath       = $runBat
    $link.WorkingDirectory = $repoDir
    $link.IconLocation     = "$exe,0"
    $link.Description      = 'Unity.Rea.Chain: 1) Deploy & Run  2) Run'
    $link.Save()
    Write-Host "    Shortcut -> $shortcut" -ForegroundColor Gray
}

# Up to date when nothing the player is built from (Assets, Packages, ProjectSettings) differs between
# the deployed commit and HEAD: a docs- or tickets-only commit doesn't rebuild the game.
$upToDate = $false
if (-not $Force -and (Test-Path (Join-Path $out $exeName)) -and (Test-Path $marker)) {
    $deployed = (Get-Content -LiteralPath $marker -Raw).Trim()
    # Under $ErrorActionPreference = 'Stop', redirecting a native command's stderr in Windows
    # PowerShell 5.1 turns every stderr line into a terminating ErrorRecord. A $marker naming a
    # commit this clone doesn't have (main rebased or force-pushed since, or deploying from a
    # different clone/worktree) makes `git diff` write "fatal: bad revision" to stderr and kills
    # the whole deploy instead of falling through to a rebuild. Confirm the commit actually
    # resolves here first, with errors temporarily non-terminating.
    $eap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    if ($deployed) { git -C $repoDir cat-file -e "$deployed^{commit}" 2>$null }
    if ($deployed -and $LASTEXITCODE -eq 0) {
        git -C $repoDir diff --quiet $deployed $head -- Assets Packages ProjectSettings
        $upToDate = $LASTEXITCODE -eq 0
    }
    $ErrorActionPreference = $eap
    if ($upToDate -and $deployed -ne $head) { Set-Content -LiteralPath $marker -Value $head -NoNewline }
}
if ($upToDate) {
    Write-Host "  Already deployed at $head (no player source changed) - skipping rebuild." -ForegroundColor Green
    # Always refreshed, so a shortcut still pointing at the old deploy.bat follows the rename.
    Set-Shortcut (Join-Path $out $exeName)
    if ($Launch) { Start-Process -FilePath (Join-Path $out $exeName) -WorkingDirectory $out }
    exit 0
}

# ── Stop running instance ──────────────────────────────────────────────────
Write-Host ''
Write-Host '  Stopping running instance...' -ForegroundColor Yellow
$procs = Get-Process 'Unity.Rea.Chain' -ErrorAction SilentlyContinue
if ($procs) {
    $procs | Stop-Process -Force
    Write-Host "    Stopped ($(@($procs).Count) process(es))" -ForegroundColor DarkYellow
    Start-Sleep -Milliseconds 800
} else {
    Write-Host '    Nothing running.' -ForegroundColor DarkGray
}

# ── Clear build output ───────────────────────────────────────────────────────
# Builds\Windows only -- never Library\, which is Unity's import/compile cache, not build output,
# and must survive a redeploy exactly as it survives a normal rebuild.
Write-Host ''
Write-Host '  Clearing build output (Builds\Windows)...' -ForegroundColor Cyan
if (Test-Path $buildDir) {
    Remove-Item -Recurse -Force $buildDir
}

# ── Build ────────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '  Building (tools\build.ps1: scene, project settings, Windows player)...' -ForegroundColor Cyan
& (Join-Path $PSScriptRoot 'build.ps1')
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed (exit $LASTEXITCODE). See Logs\build-scene.log / build-settings.log / build-player.log." -ForegroundColor Red
    exit $LASTEXITCODE
}

$builtExe = Join-Path $buildDir $exeName
if (-not (Test-Path $builtExe)) {
    Write-Host "Build completed but $exeName not found at $builtExe." -ForegroundColor Red
    exit 1
}

# ── Publish ─────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host "  Publishing $builtExe" -ForegroundColor Cyan
Write-Host "    -> $out\$exeName"

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item -Recurse -Force (Join-Path $buildDir '*') $out

$exe = Join-Path $out $exeName
if (-not (Test-Path $exe)) {
    Write-Host "Publish completed but $exeName not found at $exe." -ForegroundColor Red
    exit 1
}

Set-Content -LiteralPath $marker -Value $head -NoNewline

# ── Desktop shortcut ─────────────────────────────────────────────────────────
Set-Shortcut $exe

# ── Summary ────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '  Published successfully.' -ForegroundColor Green
Write-Host "    Exe : $exe ($([math]::Round(((Get-Item $exe).Length / 1MB), 1)) MB)" -ForegroundColor Gray
Write-Host ''

if ($Launch) {
    Write-Host '  Launching...' -ForegroundColor Cyan
    Start-Process $exe
}

# An explicit success code: nothing above sets $LASTEXITCODE for this branch, so a caller
# checking it (run.bat) would otherwise read a stale value as a failure.
exit 0
