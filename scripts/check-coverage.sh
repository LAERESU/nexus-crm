#!/bin/bash
# Gagal bila line coverage gabungan di bawah ambang (default 70%, CONTEXT.md bagian 4).
# Pemakaian: scripts/check-coverage.sh [ambang] [folder-hasil-tes]
set -euo pipefail

THRESHOLD="${1:-70}"
RESULTS="${2:-coverage}"
REPORT="${RESULTS}/report"

dotnet tool restore >/dev/null
dotnet reportgenerator \
  -reports:"${RESULTS}/**/coverage.cobertura.xml" \
  -targetdir:"${REPORT}" \
  -reporttypes:"TextSummary;MarkdownSummaryGithub" \
  -assemblyfilters:"-*Tests*" \
  -classfilters:"-*.Program" >/dev/null

line="$(grep -m1 'Line coverage:' "${REPORT}/Summary.txt")"
percent="$(echo "${line}" | sed -E 's/.*: *([0-9]+([.,][0-9]+)?)%.*/\1/' | tr ',' '.')"
echo "${line}"

if awk -v p="${percent}" -v t="${THRESHOLD}" 'BEGIN { exit !(p + 0 < t + 0) }'; then
  echo "Coverage ${percent}% di bawah ambang ${THRESHOLD}%." >&2
  exit 1
fi
echo "OK: coverage ${percent}% memenuhi ambang ${THRESHOLD}%."
