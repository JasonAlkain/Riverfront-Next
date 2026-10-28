@echo off
rem Prints the test driver's lines from every log in a nettest folder.
rem Usage: nettest-report.cmd "<log folder>"   (the scenarios call this at the end)
setlocal
if "%~1"=="" (
    echo Usage: nettest-report.cmd "Logs\nettest\<run folder>"
    exit /b 1
)
for %%f in ("%~1\*.log") do (
    echo.
    echo ===== %%~nf =====
    findstr /c:"[E2E]" "%%f" | findstr /v /c:"STATS " /c:"PERF " /c:"lobby countdown" /c:"recorders " /c:"driver up"
    findstr /c:"Exception" "%%f" >nul && echo   ^^!^^! exceptions in this log: search it for "Exception"
)
echo.
echo Bandwidth and frame-time samples are the STATS and PERF lines in each log.
endlocal
