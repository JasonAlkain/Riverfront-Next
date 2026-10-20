@echo off
rem Double-click to build the Windows client and server (close the project in Unity first).
rem Extra arguments go to build.ps1, e.g.  build.cmd -Version 0.2.0 -Targets client
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -Zip %*
pause
