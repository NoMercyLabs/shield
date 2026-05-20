@echo off
REM One-click single-file build of Shield.
REM Output: dist\win-x64\Shield.exe (self-contained, no .NET runtime needed on target).

setlocal
pushd "%~dp0"
where pwsh >nul 2>&1
if %ERRORLEVEL%==0 (
    pwsh -NoProfile -ExecutionPolicy Bypass -File "scripts\publish-singlefile.ps1" -Open
) else (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "scripts\publish-singlefile.ps1" -Open
)
set EXITCODE=%ERRORLEVEL%
popd
if not "%EXITCODE%"=="0" pause
exit /b %EXITCODE%
