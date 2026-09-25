@echo off
rem Double-click launcher for the sibling .ps1 with the same base name.
rem Deliberately kept pure ASCII: cmd.exe reads .cmd files with the ANSI codepage,
rem so a Chinese path written literally in here would be mangled.
setlocal
set "PS1FILE=%~dp0%~n0.ps1"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%PS1FILE%" %*
set "RC=%ERRORLEVEL%"
echo.
if not "%RC%"=="0" echo [exit code %RC%]
pause
exit /b %RC%
