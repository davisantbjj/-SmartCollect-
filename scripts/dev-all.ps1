param(
    [switch]$ForceInstall,
    [switch]$SkipDocker
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

if (-not (Test-Path ".env")) {
    Write-Warning "Root .env not found. Docker/API may fail without required variables."
}

if (-not $SkipDocker) {
    Write-Host "[1/1] Starting Docker services (db, api, frontend, pgadmin)..."
    docker compose up -d --build
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose up failed"
    }
} else {
    Write-Host "[1/1] Skipping Docker startup by request."
}

Write-Host "All services started in Docker!"
Write-Host "Frontend is running at http://localhost:5173"
Write-Host "To view logs, run: docker compose logs -f"
