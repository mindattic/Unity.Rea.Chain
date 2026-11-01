@echo off
rem Regenerates Assets\Scenes\Main.unity and the project settings, then builds
rem Builds\Windows\Unity.Rea.Chain.exe, each step in Unity batch mode (tools\build.ps1 does the
rem work; the Editor must be closed). Each step's run is captured to Logs\build-*.log.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\build.ps1" %*
exit /b %ERRORLEVEL%
