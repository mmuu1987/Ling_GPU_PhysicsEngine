@echo off
setlocal
cd /d "%~dp0"
if not exist "WarSandbox.exe" (
  echo Extract the entire package before starting the game.
  pause
  exit /b 1
)
if not exist "Feedback" mkdir "Feedback"
if not exist "Feedback\" (
  echo Extract this package to a writable folder.
  pause
  exit /b 1
)
:nextlog
set "PLAYTEST_LOG=%~dp0Feedback\Player-%RANDOM%-%RANDOM%.log"
if exist "%PLAYTEST_LOG%" goto nextlog
start "" "WarSandbox.exe" -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile "%PLAYTEST_LOG%"
endlocal
