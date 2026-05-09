#!/bin/bash
# post-harness-deploy.sh — Run session invalidation SQL after every harness task def registration
# Usage:
#   FAIT_DB_PASS=<password> ./scripts/post-harness-deploy.sh
#
# Required env vars:
#   FAIT_DB_PASS  — Aurora MySQL password (required)
#
# Optional env vars (defaults shown):
#   FAIT_DB_HOST  — Aurora endpoint (default: fortress-ai.c89acukue4d5.us-east-1.rds.amazonaws.com)
#   FAIT_DB_NAME  — Database name (default: fait_v2_dev)
#   FAIT_DB_USER  — DB username (default: fait_app)

set -euo pipefail

# Source deployer credentials (AWS keys)
. ~/projects/ai/projects/fortress_tools/.env.deployer

# DB connection settings
DB_HOST="${FAIT_DB_HOST:-fortress-ai.c89acukue4d5.us-east-1.rds.amazonaws.com}"
DB_NAME="${FAIT_DB_NAME:-fait_v2_dev}"
DB_USER="${FAIT_DB_USER:-fait_app}"

# Require DB password
if [[ -z "${FAIT_DB_PASS:-}" ]]; then
  echo "ERROR: FAIT_DB_PASS environment variable is required." >&2
  echo "Usage: FAIT_DB_PASS=<password> $0" >&2
  exit 1
fi

echo "[post-harness-deploy] Connecting to ${DB_HOST}/${DB_NAME} as ${DB_USER}..."

# Run invalidation SQL and capture affected rows
RESULT=$(mysql \
  -h "$DB_HOST" \
  -u "$DB_USER" \
  -p"${FAIT_DB_PASS}" \
  --batch \
  --skip-column-names \
  -e "UPDATE user_sessions SET fargate_status='Stopped', ended_at=NOW(), updated_at=NOW() WHERE fargate_status IN ('Running','Starting') AND ended_at IS NULL; SELECT ROW_COUNT();" \
  "$DB_NAME" 2>&1) || {
  echo "ERROR: DB connection or query failed." >&2
  echo "$RESULT" >&2
  exit 1
}

# Extract the row count (last line of output)
ROWS_UPDATED=$(echo "$RESULT" | tail -n1 | tr -d '[:space:]')

echo "Session invalidation complete. ${ROWS_UPDATED} rows updated."
