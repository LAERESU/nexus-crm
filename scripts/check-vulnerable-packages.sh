#!/bin/bash
# Gagal bila ada paket NuGet (termasuk transitif) dengan kerentanan High atau Critical.
# Juga gagal bila pemindaian tidak benar-benar berjalan (sumber data kerentanan tidak terjangkau),
# supaya gate tidak lolos tanpa memindai.
# Pemakaian: scripts/check-vulnerable-packages.sh [solusi]   (default: NexUS.sln)
set -euo pipefail

SOLUTION="${1:-NexUS.sln}"
OUTPUT="$(dotnet list "${SOLUTION}" package --vulnerable --include-transitive 2>&1)"
echo "${OUTPUT}"

if echo "${OUTPUT}" | grep -qE 'NU1900|vulnerability data'; then
  echo "Data kerentanan tidak dapat diambil; pemindaian tidak valid." >&2
  exit 1
fi

if ! echo "${OUTPUT}" | grep -qE 'has no vulnerable packages|has the following vulnerable packages'; then
  echo "Keluaran tidak berisi hasil pemindaian; pemindaian tidak valid." >&2
  exit 1
fi

if echo "${OUTPUT}" | grep -E '^\s*>.*\b(High|Critical)\b' >/dev/null; then
  echo "Ditemukan paket dengan kerentanan High/Critical." >&2
  exit 1
fi
echo "OK: tidak ada paket dengan kerentanan High/Critical."
