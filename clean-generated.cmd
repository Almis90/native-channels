@echo off
setlocal

where pwsh >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0eng\clean-generated.ps1" %*
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0eng\clean-generated.ps1" %*
)

set "RESULT=%ERRORLEVEL%"
echo.
if not "%RESULT%"=="0" echo Cleanup failed. See the error above.
pause
exit /b %RESULT%
