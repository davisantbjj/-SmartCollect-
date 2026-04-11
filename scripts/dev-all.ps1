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
    Write-Host "[1/3] Starting Docker services (db, api, pgadmin)..."
    docker compose up -d --build
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose up failed"
    }
} else {
    Write-Host "[1/3] Skipping Docker startup by request."
}

$shouldInstall = $ForceInstall -or -not (Test-Path "node_modules")
if ($shouldInstall) {
    Write-Host "[2/3] Installing npm dependencies..."
    npm install
    if ($LASTEXITCODE -ne 0) {
        throw "npm install failed"
    }
} else {
    Write-Host "[2/3] node_modules found. Skipping npm install."
}

Write-Host "[3/3] Starting frontend dev server..."
npm run dev
