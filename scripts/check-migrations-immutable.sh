#!/bin/bash
# Gagal bila file migrasi yang sudah ada di branch dasar diubah, dihapus, atau di-rename
# (CONTEXT.md bagian 5: perbaikan dibuat sebagai migrasi baru). Dipakai di CI (NX-006).
# Pemakaian: scripts/check-migrations-immutable.sh [branch-dasar]   (default: origin/develop)
set -euo pipefail

BASE="${1:-origin/develop}"

if ! git rev-parse --verify --quiet "${BASE}" >/dev/null; then
  echo "Branch dasar '${BASE}' tidak ditemukan." >&2
  exit 2
fi

changed="$(git diff --name-status --diff-filter=MDRT "${BASE}...HEAD" -- 'db/**/V*.sql' 'db/**/R*.sql' || true)"

if [ -n "${changed}" ]; then
  echo "File migrasi yang sudah di-merge tidak boleh diubah:" >&2
  echo "${changed}" >&2
  exit 1
fi

echo "OK: tidak ada file migrasi lama yang diubah."
