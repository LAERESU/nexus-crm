#!/bin/bash
# Gagal bila ada paket NuGet (termasuk transitif) dengan kerentanan High atau Critical.
# Pemakaian: scripts/check-vulnerable-packages.sh [solusi]   (default: NexUS.sln)
set -euo pipefail

SOLUTION="${1:-NexUS.sln}"
OUTPUT="$(dotnet list "${SOLUTION}" package --vulnerable --include-transitive 2>&1)"
echo "${OUTPUT}"

if echo "${OUTPUT}" | grep -E '^\s*>.*\b(High|Critical)\b' >/dev/null; then
  echo "Ditemukan paket dengan kerentanan High/Critical." >&2
  exit 1
fi
echo "OK: tidak ada paket dengan kerentanan High/Critical."
