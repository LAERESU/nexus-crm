#!/bin/bash
# Dijalankan otomatis oleh image gvenzl/oracle-free saat database pertama kali dibuat.
# Membuat satu user (= schema) per service dengan hak minimal. Password diambil dari
# environment (ORACLE_APP_PASSWORD) agar tidak ada secret di repo.
set -euo pipefail

SCHEMAS=(AUTH_SCHEMA USR_SCHEMA PRJ_SCHEMA TSK_SCHEMA SOP_SCHEMA NTF_SCHEMA AUD_SCHEMA)

for schema in "${SCHEMAS[@]}"; do
  sqlplus -s / as sysdba <<SQL
WHENEVER SQLERROR EXIT SQL.SQLCODE
ALTER SESSION SET CONTAINER = FREEPDB1;
CREATE USER ${schema} IDENTIFIED BY "${ORACLE_APP_PASSWORD}" DEFAULT TABLESPACE USERS QUOTA UNLIMITED ON USERS;
GRANT CREATE SESSION, CREATE TABLE, CREATE SEQUENCE, CREATE VIEW, CREATE PROCEDURE, CREATE TRIGGER TO ${schema};
EXIT;
SQL
  echo "Schema ${schema} dibuat"
done
