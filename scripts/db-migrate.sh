#!/bin/bash
# Menjalankan Flyway untuk semua schema lewat docker compose.
# Pemakaian: scripts/db-migrate.sh [migrate|validate|info|repair]   (default: migrate)
set -euo pipefail

COMMAND="${1:-migrate}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

cd "${ROOT}/deploy"
docker compose up -d oracle --wait
docker compose run --rm flyway "${COMMAND}"
