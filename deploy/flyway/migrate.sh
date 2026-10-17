#!/bin/sh
# Menjalankan perintah Flyway (default: migrate) untuk setiap schema service.
# Pemetaan folder db/<dir> -> user schema Oracle. Password dibaca dari ORACLE_APP_PASSWORD.
set -eu

COMMAND="${1:-migrate}"
URL="${FLYWAY_ORACLE_URL:-jdbc:oracle:thin:@//oracle:1521/FREEPDB1}"

for entry in auth:AUTH_SCHEMA usr:USR_SCHEMA prj:PRJ_SCHEMA tsk:TSK_SCHEMA sop:SOP_SCHEMA ntf:NTF_SCHEMA aud:AUD_SCHEMA; do
  dir="${entry%%:*}"
  schema="${entry##*:}"
  echo "==> ${schema}: flyway ${COMMAND}"
  flyway \
    -url="${URL}" \
    -user="${schema}" \
    -password="${ORACLE_APP_PASSWORD}" \
    -locations="filesystem:/flyway/db/${dir}" \
    -connectRetries=30 \
    -validateMigrationNaming=true \
    "${COMMAND}"
done
