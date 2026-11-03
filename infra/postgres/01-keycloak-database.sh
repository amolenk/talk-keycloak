#!/usr/bin/env bash
set -euo pipefail

: "${POSTGRES_USER:?PostgreSQL bootstrap username is required}"
: "${KEYCLOAK_DB_PASSWORD:?Set the Keycloak database password parameter}"

psql --set=ON_ERROR_STOP=1 \
  --username "$POSTGRES_USER" \
  --dbname postgres \
  --set=keycloak_password="$KEYCLOAK_DB_PASSWORD" <<'SQL'
CREATE ROLE keycloak LOGIN PASSWORD :'keycloak_password';
CREATE DATABASE keycloak OWNER keycloak;
SQL
