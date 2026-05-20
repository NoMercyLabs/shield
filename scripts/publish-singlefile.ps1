# Publishes Shield.Api as a single self-contained .exe — no .NET runtime install needed
# on the target machine. SPA wwwroot + appsettings are bundled inside the binary.
#
# Usage:
#   pwsh scripts/publish-singlefile.ps1                  # win-x64 (default)
#   pwsh scripts/publish-singlefile.ps1 -Rid linux-x64   # cross-publish to Linux
#   pwsh scripts/publish-singlefile.ps1 -Open            # open output dir when done
#
# Output: dist/<rid>/Shield.exe (Windows) or dist/<rid>/Shield (Linux/macOS)

[CmdletBinding()]
param(
    [string]$Rid = 'win-x64',
    [string]$Configuration = 'Release',
    [switch]$SkipSpa,
    [switch]$Open
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$webDir = Join-Path $root 'src/Shield.Web'
$apiCsproj = Join-Path $root 'src/Shield.Api/Shield.Api.csproj'
$outDir = Join-Path $root "dist/$Rid"

if (-not $SkipSpa -and (Test-Path $webDir)) {
    Write-Host "[1/2] Building SPA → wwwroot..." -ForegroundColor Cyan
    Push-Location $webDir
    try {
        if (-not (Test-Path 'node_modules')) {
            & npm ci
            if ($LASTEXITCODE -ne 0) { throw "npm ci failed (exit $LASTEXITCODE)" }
        }
        & npm run build
        if ($LASTEXITCODE -ne 0) { throw "SPA build failed (exit $LASTEXITCODE)" }
    } finally {
        Pop-Location
    }
}

Write-Host "[2/2] Publishing single-file .exe ($Rid, $Configuration)..." -ForegroundColor Cyan
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }

& dotnet publish $apiCsproj `
    -c $Configuration `
    -r $Rid `
    --self-contained `
    -p:PublishSingleFile=true `
    -o $outDir `
    --nologo

if ($LASTEXITCODE -ne 0) { throw "Publish failed (exit $LASTEXITCODE)" }

# Rename the produced binary from Shield.Api(.exe) to Shield(.exe). Can't use
# -p:AssemblyName=Shield in the publish call because there's another project called
# Shield in the solution, which makes the assembly-name reference ambiguous to MSBuild.
$publishedName = if ($Rid -like 'win-*') { 'Shield.Api.exe' } else { 'Shield.Api' }
$publishedPath = Join-Path $outDir $publishedName
$exeName = if ($Rid -like 'win-*') { 'Shield.exe' } else { 'Shield' }
$exePath = Join-Path $outDir $exeName
if (Test-Path $publishedPath) {
    if (Test-Path $exePath) { Remove-Item $exePath -Force }
    Move-Item $publishedPath $exePath -Force
}

if (Test-Path $exePath) {
    # Strip publish debris that ships next to the exe — .pdb files (incl. the 80 MB libSkiaSharp.pdb),
    # the IIS-only web.config, and the staticwebassets manifest aren't needed at runtime when the SPA
    # lives inside the exe via ManifestEmbeddedFileProvider.
    Get-ChildItem $outDir -Filter '*.pdb' | Remove-Item -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $outDir 'web.config') -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $outDir 'Shield.Api.staticwebassets.endpoints.json') -ErrorAction SilentlyContinue
    if (Test-Path (Join-Path $outDir 'wwwroot')) {
        Remove-Item (Join-Path $outDir 'wwwroot') -Recurse -Force -ErrorAction SilentlyContinue
    }

    # Copy the one-click launcher into the dist root (one level above the rid-specific folder).
    $launcherTemplate = Join-Path $root 'scripts/Run-Shield.template.cmd'
    if (Test-Path $launcherTemplate) {
        $launcherDest = Join-Path (Split-Path $outDir -Parent) 'Run-Shield.cmd'
        Copy-Item $launcherTemplate $launcherDest -Force
    }
    # Also drop the secrets example alongside so operators see the format. Live secrets.cmd
    # under dist/data/ is never overwritten because it's gitignored + outside the publish path.
    $secretsExample = Join-Path $root 'scripts/secrets.cmd.example'
    if (Test-Path $secretsExample) {
        $secretsDest = Join-Path (Split-Path $outDir -Parent) 'secrets.cmd.example'
        Copy-Item $secretsExample $secretsDest -Force
    }

    $size = [math]::Round((Get-Item $exePath).Length / 1MB, 1)
    Write-Host ""
    Write-Host "Done. Single-file binary: $exePath ($size MB)" -ForegroundColor Green
    Write-Host "Launcher: $(Split-Path $outDir -Parent)\Run-Shield.cmd" -ForegroundColor DarkGray
    Write-Host "Direct: $exePath --urls http://localhost:8842" -ForegroundColor DarkGray
    if ($Open) { Start-Process explorer.exe $outDir }
} else {
    throw "Publish completed but $exePath was not produced."
}
