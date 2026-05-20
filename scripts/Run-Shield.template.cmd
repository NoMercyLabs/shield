@echo off
REM One-click Shield launcher. Sets sensible defaults so a fresh extract just works.
REM Override anything below by setting the env var before running this script.

setlocal
pushd "%~dp0win-x64"

if "%Shield__Auth__DataProtectionMasterKey%"=="" (
    echo [Shield] Shield__Auth__DataProtectionMasterKey is not set.
    echo [Shield] Set it to a stable 32-byte base64 string before first run, otherwise
    echo [Shield] stored secrets would be lost on every restart. Aborting.
    pause
    exit /b 1
)

if "%DOTNET_ENVIRONMENT%"=="" set DOTNET_ENVIRONMENT=Production
if "%Shield__Db__Shield%"=="" set Shield__Db__Shield=Data Source=%~dp0data\shield.db
if "%Shield__Db__Feeds%"=="" set Shield__Db__Feeds=Data Source=%~dp0data\feeds.db
if "%Shield__Auth__DataProtectionKeysPath%"=="" set Shield__Auth__DataProtectionKeysPath=%~dp0data\keys
if "%SHIELD_PORT%"=="" set SHIELD_PORT=8842

if not exist "%~dp0data" mkdir "%~dp0data"

echo [Shield] Starting on http://localhost:%SHIELD_PORT%
echo [Shield] Press Ctrl+C to stop.
echo.
start "" http://localhost:%SHIELD_PORT%
Shield.exe --urls http://localhost:%SHIELD_PORT%

popd
endlocal
