@echo off
rem Asks first: 1) Deploy & Run pulls latest, always rebuilds, publishes to C:\Apps\Unity.Rea.Chain,
rem refreshes the Desktop shortcut and launches it (tools\run.ps1 -Force -Launch does the work);
rem 2) Run just launches the copy already in C:\Apps\Unity.Rea.Chain.
setlocal
set "EXE=C:\Apps\Unity.Rea.Chain\Unity.Rea.Chain.exe"
echo.
echo   Unity.Rea.Chain
echo     1^) Deploy ^& Run
echo     2^) Run
echo.
choice /c 12 /n /m "  Choose 1 or 2: "
if errorlevel 2 goto run

:deploy
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\run.ps1" -Force -Launch %*
if errorlevel 1 (
    echo.
    echo   Deploy failed. See Logs\build-scene.log, build-settings.log or build-player.log.
    if exist "%EXE%" (
        echo   Launching the installed copy instead.
        start "" /d "C:\Apps\Unity.Rea.Chain" "%EXE%"
    )
    pause
    exit /b 1
)
timeout /t 5
exit /b 0

:run
if not exist "%EXE%" (
    echo   Nothing is deployed to C:\Apps\Unity.Rea.Chain yet; deploying first.
    goto deploy
)
start "" /d "C:\Apps\Unity.Rea.Chain" "%EXE%" %*
