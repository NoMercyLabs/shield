#!/usr/bin/env pwsh
# Shield end-to-end smoke test (Windows / PowerShell 7+).
# Mirror of scripts/smoke-test.sh — boots the API in single-user mode, walks the
# public surface, tears down. Exits 0 on success, non-zero on first failure.

[CmdletBinding()]
param(
    [int]$Port = 5099
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $repoRoot

$base       = "http://localhost:$Port"
$fixtureDir = Join-Path $repoRoot 'tests/fixtures/smoke-fixture'
# SQLite resolves "Data Source=data/..." relative to the process CWD; we launch
# the API from the project dir, so the data dir lands under src/Shield.Api/data.
$dataDir    = Join-Path $repoRoot 'src/Shield.Api/data'
$logFile    = Join-Path $repoRoot 'shield-smoke.log'
$session    = $null
$serverProc = $null

function Write-Step([string]$msg)  { Write-Host "[smoke] $msg"      -ForegroundColor Cyan }
function Write-Note([string]$msg)  { Write-Host "  $msg"            -ForegroundColor DarkGray }
function Write-Warn([string]$msg)  { Write-Host "[smoke] $msg"      -ForegroundColor Yellow }
function Fail([string]$msg) {
    Write-Host "[smoke FAIL] $msg" -ForegroundColor Red
    throw $msg
}

function Stop-Server {
    if ($script:serverProc -and -not $script:serverProc.HasExited) {
        Write-Step "stopping API (pid $($script:serverProc.Id))"
        try {
            Stop-Process -Id $script:serverProc.Id -Force -ErrorAction Stop
        } catch {
            Write-Warn "Stop-Process failed: $($_.Exception.Message)"
        }
    }
}

function Cleanup {
    Stop-Server
    if (Test-Path $dataDir) {
        Write-Step "removing smoke data dir $dataDir"
        Remove-Item -Recurse -Force $dataDir -ErrorAction SilentlyContinue
    }
}

try {
    foreach ($tool in @('dotnet','npm','node')) {
        if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
            Fail "required tool '$tool' not on PATH"
        }
    }

    Write-Step "step 1/9 — building Shield.Web SPA"
    Push-Location (Join-Path $repoRoot 'src/Shield.Web')
    try {
        # npm ci wipes node_modules; on Windows that races with locked .node binaries
        # from any concurrent Node process. Fall through to npm install if ci fails.
        & npm ci
        if ($LASTEXITCODE -ne 0) {
            Write-Warn "npm ci failed (often a Windows file-lock); falling back to npm install"
            & npm install
            if ($LASTEXITCODE -ne 0) { Fail "npm install failed" }
        }
        & npm run build
        if ($LASTEXITCODE -ne 0) { Fail "npm run build failed" }
    } finally {
        Pop-Location
    }

    Write-Step "step 2/9 — starting Shield.Api on $base"
    Set-Content -Path $logFile -Value '' -Encoding UTF8
    # Run the published DLL directly so the PID we own is the real host process,
    # not a `dotnet run` parent that orphans its socket-owning child on Windows.
    $apiDll        = Join-Path $repoRoot 'src/Shield.Api/bin/Release/net9.0/Shield.Api.dll'
    $apiProjectDir = Join-Path $repoRoot 'src/Shield.Api'
    if (-not (Test-Path $apiDll)) { Fail "expected built API at $apiDll — run 'dotnet build -c Release' first" }
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName               = 'dotnet'
    $startInfo.ArgumentList.Add($apiDll)        | Out-Null
    $startInfo.ArgumentList.Add('--urls')       | Out-Null
    $startInfo.ArgumentList.Add($base)          | Out-Null
    # --contentRoot points appsettings.json + wwwroot at the project dir.
    $startInfo.ArgumentList.Add('--contentRoot') | Out-Null
    $startInfo.ArgumentList.Add($apiProjectDir)  | Out-Null
    # WorkingDirectory = project dir so the relative SQLite "data/" path resolves.
    if (-not (Test-Path $dataDir)) { New-Item -ItemType Directory -Path $dataDir | Out-Null }
    $startInfo.WorkingDirectory       = $apiProjectDir
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError  = $true
    $startInfo.UseShellExecute        = $false
    # Production env matches release.yml container behaviour and avoids ASP.NET's
    # Development scope validator tripping on the Singleton-vs-Scoped channel
    # registration bug (see report).
    $startInfo.Environment['ASPNETCORE_ENVIRONMENT'] = 'Production'

    $script:serverProc = [System.Diagnostics.Process]::Start($startInfo)
    Write-Note "api pid=$($serverProc.Id), logs=$logFile"

    # Drain stdout/stderr asynchronously so the process doesn't deadlock.
    $stdoutJob = Start-Job -ArgumentList @($serverProc.Id, $logFile) -ScriptBlock {
        param($pid, $log)
        $proc = Get-Process -Id $pid -ErrorAction SilentlyContinue
        if (-not $proc) { return }
        while (-not $proc.HasExited) {
            $line = $proc.StandardOutput.ReadLine()
            if ($null -ne $line) { Add-Content -Path $log -Value $line }
        }
    }

    Write-Step "step 3/9 — waiting up to 30s for /healthz"
    $healthOk = $false
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        try {
            $resp = Invoke-WebRequest -Uri "$base/healthz" -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop
            if ($resp.StatusCode -eq 200) {
                Write-Note "healthz ok after ${attempt}s"
                $healthOk = $true
                break
            }
        } catch {
            # not up yet — keep polling
        }
        if ($serverProc.HasExited) {
            Fail "API process died during startup (exit=$($serverProc.ExitCode), see $logFile)"
        }
        Start-Sleep -Seconds 1
    }
    if (-not $healthOk) { Fail "/healthz never returned 200 within 30s" }

    Write-Step "step 4/9 — GET /api/auth/me (single-user mode)"
    $session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $me = Invoke-RestMethod -Uri "$base/api/auth/me" -Method Get -WebSession $session
    Write-Note ("body: " + ($me | ConvertTo-Json -Compress))
    if (-not $me.username) { Fail "/api/auth/me returned empty username" }
    if (-not $me.singleUserMode) { Fail "/api/auth/me did not report singleUserMode=true" }
    Write-Note "authenticated as '$($me.username)' (singleUserMode=$($me.singleUserMode))"

    Write-Step "step 5/9 — POST /api/sources (LocalFolder pointing at fixture)"
    # SourceType.LocalFolder = 1, ScanInterval is TimeSpan ("hh:mm:ss"),
    # ConfigJson is a *string* containing the LocalFolderConfig payload.
    $configJson = (@{ path = $fixtureDir } | ConvertTo-Json -Compress)
    $createBody = @{
        type         = 1
        name         = 'smoke-fixture'
        configJson   = $configJson
        scanInterval = '01:00:00'
        enabled      = $true
    } | ConvertTo-Json
    $createResp = Invoke-RestMethod -Uri "$base/api/sources" -Method Post `
        -ContentType 'application/json' -Body $createBody -WebSession $session
    $sourceId = $createResp.id
    if (-not $sourceId) { Fail "create response missing id: $($createResp | ConvertTo-Json -Compress)" }
    Write-Note "created source id=$sourceId"

    Write-Step "step 6/9 — POST /api/sources/$sourceId/scan-now"
    $scanResp = Invoke-WebRequest -Uri "$base/api/sources/$sourceId/scan-now" -Method Post `
        -WebSession $session -UseBasicParsing
    if ($scanResp.StatusCode -ne 202) { Fail "scan-now returned HTTP $($scanResp.StatusCode) (expected 202)" }

    Write-Step "step 7/9 — waiting up to 15s for snapshot"
    $snapshotOk = $false
    for ($attempt = 1; $attempt -le 15; $attempt++) {
        try {
            $detail = Invoke-RestMethod -Uri "$base/api/sources/$sourceId" -Method Get -WebSession $session
            if ($detail.lastSnapshot -and $detail.lastSnapshot.id) {
                Write-Note "snapshot $($detail.lastSnapshot.id) with $($detail.lastSnapshot.itemCount) item(s) after ${attempt}s"
                $snapshotOk = $true
                break
            }
        } catch {
            # source not yet readable on this poll — keep trying
        }
        Start-Sleep -Seconds 1
    }
    if (-not $snapshotOk) { Fail "source $sourceId never produced a snapshot within 15s" }

    if ($env:DISCORD_WEBHOOK_URL) {
        Write-Step "step 8/9 — DISCORD_WEBHOOK_URL set, creating Discord channel"
        $chConfig = (@{ webhookUrl = $env:DISCORD_WEBHOOK_URL } | ConvertTo-Json -Compress)
        $chBody = @{
            type        = 0
            name        = 'smoke-discord'
            configJson  = $chConfig
            minSeverity = 0
            enabled     = $true
        } | ConvertTo-Json
        $ch = Invoke-RestMethod -Uri "$base/api/channels" -Method Post `
            -ContentType 'application/json' -Body $chBody -WebSession $session
        if (-not $ch.id) { Fail "channel create response missing id" }
        Write-Note "created channel id=$($ch.id)"

        Write-Step "  POST /api/channels/$($ch.id)/test-send"
        $testResp = Invoke-WebRequest -Uri "$base/api/channels/$($ch.id)/test-send" -Method Post `
            -WebSession $session -UseBasicParsing
        if ($testResp.StatusCode -ne 200) { Fail "test-send returned HTTP $($testResp.StatusCode) (expected 200)" }
        Write-Note "discord test-send ok"
    } else {
        Write-Step "step 8/9 — DISCORD_WEBHOOK_URL not set, skipping Discord channel check"
    }

    Write-Step "step 9/9 — done, smoke PASSED"
    $exitCode = 0
}
catch {
    Write-Host "[smoke FAIL] $($_.Exception.Message)" -ForegroundColor Red
    if (Test-Path $logFile) {
        Write-Warn "last 60 lines of $logFile:"
        Get-Content $logFile -Tail 60 | ForEach-Object { Write-Host $_ -ForegroundColor DarkGray }
    }
    $exitCode = 1
}
finally {
    Cleanup
    if ($stdoutJob) {
        Stop-Job $stdoutJob -ErrorAction SilentlyContinue | Out-Null
        Remove-Job $stdoutJob -Force -ErrorAction SilentlyContinue | Out-Null
    }
}

exit $exitCode
