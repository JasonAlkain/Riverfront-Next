@echo off
rem LAN by IP against a headless dedicated server, all on this PC:
rem   alice   picks Heavy in the lobby, then changes to Engineer at a friendly post
rem   bob     switches team in the lobby and leaves 40 s into the match
rem   carol   joins late, 60 s after the others
rem Bots fill both teams; tickets are low so the match ends in a few minutes. Everyone
rem quits by themselves after the results and the return to the lobby.
setlocal
set SCENARIO=lan
call "%~dp0nettest-common.cmd" || exit /b 1
set "PORT=7790"

start "server" "%SERVER%" -batchmode -nographics -logFile "%LOGS%\server.log" -e2e -port %PORT% -map the_frontline -tickets 20 -teamSize 4 -bots 1 -minPlayers 2 -startCountdown 5 -name "Nettest LAN" -e2eStatsEvery 10 -e2eQuitAfter 480
timeout /t 5 /nobreak >nul
start "alice" "%CLIENT%" %WINDOW% -logFile "%LOGS%\alice.log" -e2e -e2eName alice -e2eAuthProfile alice -e2eJoin 127.0.0.1:%PORT% -e2eClass 1 -e2eChangeClassAtPost 2
start "bob" "%CLIENT%" %WINDOW% -logFile "%LOGS%\bob.log" -e2e -e2eName bob -e2eAuthProfile bob -e2eJoin 127.0.0.1:%PORT% -e2eSwitchTeam -e2eLeaveAfter 40 -e2eQuitAfter 90
timeout /t 60 /nobreak >nul
start "carol" "%CLIENT%" %WINDOW% -logFile "%LOGS%\carol.log" -e2e -e2eName carol -e2eAuthProfile carol -e2eJoin 127.0.0.1:%PORT%

echo.
echo Running. Leave the windows alone (a click in one fires its weapon). When they have all
echo closed by themselves (about 5 minutes; the server closes after 8), press a key for the summary.
pause
call "%~dp0nettest-report.cmd" "%LOGS%"
pause
endlocal
