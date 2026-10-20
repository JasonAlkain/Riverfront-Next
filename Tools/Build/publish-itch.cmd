@echo off
rem Double-click to push the newest build to itch.io.
rem Extra arguments go to publish-itch.ps1, e.g.  publish-itch.cmd -DryRun
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish-itch.ps1" %*
pause
