@echo off
rem Launches a playable Builds\Windows\Unity.Rea.Chain.exe for development, muted by default
rem (-muted / MINDATTIC_CLI_MUTED=1, Unity.Shared's session mute; pass -Sound for audio).
rem Builds it first with tools\build.ps1 if missing (tools\play.ps1 does the work; same shape
rem as Unity.Rea.QuickDuel's play.ps1).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\play.ps1" %*
exit /b %ERRORLEVEL%
