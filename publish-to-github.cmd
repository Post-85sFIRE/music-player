@echo off
REM Wrapper to bypass PowerShell Restricted execution policy.
REM Double-click this file OR run from PowerShell:  .\publish-to-github.cmd
REM Stays open at the end (pause) so you can see the result.
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish-to-github.ps1" %*
set EXITCODE=%errorlevel%
echo.
echo ============================================================
echo ExitCode = %EXITCODE%
echo ============================================================
if "%EXITCODE%" NEQ "0" (
    echo Something went wrong. Read the lines above for the error.
)
echo Press any key to close this window . . .
pause >nul
