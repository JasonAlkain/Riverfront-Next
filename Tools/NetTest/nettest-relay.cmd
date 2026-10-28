@echo off
rem Online play through Unity Relay (needs internet), all on this PC:
rem   host     hosts an online match and lists it in the Browse list
rem   coder    joins with the host's join code
rem   browser  finds the match in the Browse list and joins it from there
rem   wrong    tries a join code that doesn't exist and should get a clear message
setlocal enabledelayedexpansion
set SCENARIO=relay
call "%~dp0nettest-common.cmd" || exit /b 1
set "SESSION=Nettest Relay %STAMP%"

start "host" "%CLIENT%" %WINDOW% -logFile "%LOGS%\host.log" -e2e -e2eName host -e2eAuthProfile host -e2eHostOnline -e2eSession "%SESSION%" -e2eTickets 12 -e2eTeamSize 4 -e2eStartWhen 3
start "wrong" "%CLIENT%" %WINDOW% -logFile "%LOGS%\wrong.log" -e2e -e2eName wrong -e2eAuthProfile wrong -e2eJoinCode ZZZZ99 -e2eQuitAfter 30

rem Wait for the host's join code to appear in its log.
set "CODE="
for /l %%i in (1,1,30) do (
    if not defined CODE (
        timeout /t 2 /nobreak >nul
        for /f "tokens=*" %%l in ('findstr /c:"host online OK" "%LOGS%\host.log" 2^>nul') do set "LINE=%%l"
        if defined LINE set "CODE=!LINE:~-6!"
    )
)
if not defined CODE (
    echo The host did not get a join code in 60 s. See "%LOGS%\host.log".
    pause
    exit /b 1
)
echo Join code: %CODE%
start "coder" "%CLIENT%" %WINDOW% -logFile "%LOGS%\coder.log" -e2e -e2eName coder -e2eAuthProfile coder -e2eJoinCode %CODE%
timeout /t 15 /nobreak >nul
start "browser" "%CLIENT%" %WINDOW% -logFile "%LOGS%\browser.log" -e2e -e2eName browser -e2eAuthProfile browser -e2eBrowse "%SESSION%"

echo.
echo Running. Leave the windows alone (a click in one fires its weapon). When they have all
echo closed by themselves (about 5 minutes), press a key for the summary.
pause
call "%~dp0nettest-report.cmd" "%LOGS%"
pause
endlocal
