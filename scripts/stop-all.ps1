param(
    [switch]$PruneVolumes
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

Write-Host "Stopping Docker services..."

if ($PruneVolumes) {
    docker compose down -v
} else {
    docker compose down
}

if ($LASTEXITCODE -ne 0) {
    throw "docker compose down failed"
}

Write-Host "Services stopped."
