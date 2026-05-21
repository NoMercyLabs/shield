@echo off
REM One-click Shield launcher. Defaults to Development env so the DataProtection master
REM key isn't required — keys persist unprotected to data\keys\, matching the dev-mode
REM dotnet-run flow. To deploy in real production, set DOTNET_ENVIRONMENT=Production AND
REM Shield__Auth__DataProtectionMasterKey to a stable 32-byte base64 value BEFORE running.

setlocal
pushd "%~dp0win-x64"

if not exist "%~dp0data" mkdir "%~dp0data"

REM Optional per-install secrets file. Kept under data\ so it travels with the DB and not
REM with the dist/ checkout. Typical contents:
REM   set Shield__Feeds__Ghsa__Pat=ghp_yourPersonalAccessToken
REM   set Shield__Channels__Smtp__Password=...
REM Anyone with read access to this file can act as Shield's outbound integrations — keep
REM it next to the database it serves and back it up the same way.
if exist "%~dp0data\secrets.cmd" call "%~dp0data\secrets.cmd"

if "%DOTNET_ENVIRONMENT%"=="" set DOTNET_ENVIRONMENT=Development
if "%Shield__Db__Shield%"=="" set Shield__Db__Shield=Data Source=%~dp0data\shield.db
if "%Shield__Db__Feeds%"=="" set Shield__Db__Feeds=Data Source=%~dp0data\feeds.db
if "%Shield__Auth__DataProtectionKeysPath%"=="" set Shield__Auth__DataProtectionKeysPath=%~dp0data\keys
if "%SHIELD_PORT%"=="" set SHIELD_PORT=8842

REM One-click launch opens the browser AFTER Kestrel is listening. The host owns this;
REM the launcher just opts in. To skip browser open, set Shield__Launcher__OpenBrowser=false
REM in data\secrets.cmd. To open on a public/proxied URL, set Shield__Launcher__Url there.
if "%Shield__Launcher__OpenBrowser%"=="" set Shield__Launcher__OpenBrowser=true

REM Quiet the EF Core / framework debug spam that ships with Development env defaults so
REM the console stays readable. Override per-category if you need to diagnose a specific
REM subsystem (e.g. set Logging__LogLevel__Shield=Debug before running).
if "%Logging__LogLevel__Default%"=="" set Logging__LogLevel__Default=Warning
if "%Logging__LogLevel__Shield%"=="" set Logging__LogLevel__Shield=Information
if "%Logging__LogLevel__Microsoft%"=="" set Logging__LogLevel__Microsoft=Warning
if "%Logging__LogLevel__Microsoft.Hosting.Lifetime%"=="" set Logging__LogLevel__Microsoft.Hosting.Lifetime=Information
if "%Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics%"=="" set Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics=Warning

REM Bind on all interfaces so reverse proxies on the LAN (e.g. Caddy at 192.168.2.201)
REM can forward to this box. The browser still opens on localhost — that's just the URL
REM the user lands on; the listener accepts every IP that resolves to this host.
set ASPNETCORE_URLS=http://+:%SHIELD_PORT%

echo [Shield] Listening on %ASPNETCORE_URLS%
echo [Shield] Press Ctrl+C to stop.
echo.
Shield.exe

popd
endlocal
