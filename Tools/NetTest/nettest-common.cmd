@echo off
rem Shared setup for the nettest-*.cmd scenarios. Not meant to be run by itself.
rem Finds the newest development build under Builds\ (or uses NETTEST_BUILD) and a log folder.

set "ROOT=%~dp0..\.."
if defined NETTEST_BUILD (
    set "BUILD=%NETTEST_BUILD%"
) else (
    for /f "delims=" %%d in ('dir /b /ad /o-d "%ROOT%\Builds" 2^>nul') do (
        set "BUILD=%ROOT%\Builds\%%d"
        goto :found
    )
)
:found
if not exist "%BUILD%\windows-client\Riverfront Next.exe" (
    echo No client build found in "%BUILD%". Build one first with:  Tools\Build\build.cmd -Development
    exit /b 1
)
findstr /c:"development=true" "%BUILD%\build-info.txt" >nul 2>&1
if errorlevel 1 (
    echo "%BUILD%" is not a development build; the test driver only exists in development builds.
    echo Build one with:  Tools\Build\build.cmd -Development
    exit /b 1
)

set "CLIENT=%BUILD%\windows-client\Riverfront Next.exe"
set "SERVER=%BUILD%\windows-server\Riverfront Next.exe"
for /f %%t in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMdd-HHmmss"') do set "STAMP=%%t"
set "LOGS=%ROOT%\Logs\nettest\%SCENARIO%-%STAMP%"
mkdir "%LOGS%" >nul 2>&1
rem Small windows so several clients fit on one screen.
set "WINDOW=-screen-fullscreen 0 -screen-width 640 -screen-height 360"
echo Build : %BUILD%
echo Logs  : %LOGS%
exit /b 0
