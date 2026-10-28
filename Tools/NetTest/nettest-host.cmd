@echo off
rem LAN by IP against a listen-server host (a normal client build that hosts), all on this PC:
rem   host    hosts on port 7777 and starts once 3 players are in the lobby; plays 2 matches
rem   alice   joins through a simulated bad network (150 ms delay, 30 ms jitter, 3% loss); 2 matches
rem   bob     joins normally; 2 matches (the second match is the rematch from the lobby)
setlocal
set SCENARIO=host
call "%~dp0nettest-common.cmd" || exit /b 1

start "host" "%CLIENT%" %WINDOW% -logFile "%LOGS%\host.log" -e2e -e2eName host -e2eAuthProfile host -e2eHost -e2eMap the_frontline -e2eTickets 15 -e2eTeamSize 4 -e2eStartWhen 3 -e2eMatches 2
timeout /t 6 /nobreak >nul
start "alice" "%CLIENT%" %WINDOW% -logFile "%LOGS%\alice.log" -e2e -e2eName alice -e2eAuthProfile alice -e2eJoin 127.0.0.1:7777 -e2eMatches 2 -e2eNetSim 150,30,3
start "bob" "%CLIENT%" %WINDOW% -logFile "%LOGS%\bob.log" -e2e -e2eName bob -e2eAuthProfile bob -e2eJoin 127.0.0.1:7777 -e2eMatches 2

echo.
echo Running two matches. Leave the windows alone (a click in one fires its weapon). When they
echo have all closed by themselves (about 8 minutes), press a key for the summary.
pause
call "%~dp0nettest-report.cmd" "%LOGS%"
pause
endlocal
