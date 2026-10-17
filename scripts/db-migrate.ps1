# Menjalankan Flyway untuk semua schema lewat docker compose.
# Pemakaian: scripts/db-migrate.ps1 [-Command migrate|validate|info|repair]   (default: migrate)
param(
    [ValidateSet('migrate', 'validate', 'info', 'repair')]
    [string]$Command = 'migrate'
)

$ErrorActionPreference = 'Stop'
$deploy = Join-Path $PSScriptRoot '..\deploy'

Push-Location $deploy
try {
    docker compose up -d oracle --wait
    if ($LASTEXITCODE -ne 0) { throw 'Oracle gagal siap.' }
    docker compose run --rm flyway $Command
    if ($LASTEXITCODE -ne 0) { throw "Flyway $Command gagal." }
}
finally {
    Pop-Location
}
