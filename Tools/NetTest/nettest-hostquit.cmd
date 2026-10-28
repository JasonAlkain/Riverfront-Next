@echo off
rem The host quits mid-match: both clients must land back on the main menu with a message.
setlocal
set SCENARIO=hostquit
call "%~dp0nettest-common.cmd" || exit /b 1

start "host" "%CLIENT%" %WINDOW% -logFile "%LOGS%\host.log" -e2e -e2eName host -e2eAuthProfile host -e2eHost -e2eTickets 50 -e2eStartWhen 3 -e2eQuitAfter 60
timeout /t 6 /nobreak >nul
start "alice" "%CLIENT%" %WINDOW% -logFile "%LOGS%\alice.log" -e2e -e2eName alice -e2eAuthProfile alice -e2eJoin 127.0.0.1:7777 -e2eQuitAfter 100
start "bob" "%CLIENT%" %WINDOW% -logFile "%LOGS%\bob.log" -e2e -e2eName bob -e2eAuthProfile bob -e2eJoin 127.0.0.1:7777 -e2eQuitAfter 100

echo.
echo Running. The host closes after 60 s; the clients should show the main menu, then close at 100 s.
echo When all windows have closed, press a key for the summary.
pause
call "%~dp0nettest-report.cmd" "%LOGS%"
pause
endlocal
