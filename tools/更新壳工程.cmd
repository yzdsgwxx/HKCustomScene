@echo off
rem Double-click launcher for the sibling .ps1 (same base name).
rem Pure ASCII on purpose: cmd.exe reads .cmd with the ANSI codepage,
rem so a Chinese path written literally in here would get mangled.
rem
rem Behaviour: the console closes itself as soon as the script finishes.
rem Only on a NON-ZERO exit code does it linger 20 seconds (or any key) so the
rem error stays readable. The complete output is always saved to tools\_log\*.log
setlocal
set "PS1FILE=%~dp0%~n0.ps1"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%PS1FILE%" %*
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" (
  echo.
  echo [FAILED  exit code %RC%]  closing in 20s ^(any key closes now^)
  echo full output: %~dp0_log\
  timeout /t 20 >nul 2>&1
)
exit /b %RC%
