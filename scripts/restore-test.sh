#!/usr/bin/env bash
# =====================================================================
# Batch 52 (OPS-01, OPS-05): restore test for a backup made by
# scripts/backup.sh. Production is never touched: the database is restored
# into a throw-away SQL Server container without network access, and the
# uploads archive is unpacked into a temporary directory.
#
#   scripts/restore-test.sh                       # newest backup in $BACKUP_ROOT
#   scripts/restore-test.sh /var/backups/onlinestore/20261111_020000
#
# Checks (any failure exits non-zero):
#   1. SHA256SUMS match.
#   2. The database restores, DBCC CHECKDB reports no error, and the last EF
#      migration equals the one recorded in manifest.txt.
#   3. The uploads archive unpacks and holds the number of files recorded
#      in manifest.txt.
# Record the printed summary in docs/runbook.md ("Restore test log").
#
# Environment (all optional):
#   BACKUP_ROOT              default /var/backups/onlinestore
#   MSSQL_IMAGE              default mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04
#                            (same or newer version than production)
#   RESTORE_TEST_MSSQL_PID   default Express
#   KEEP_RESTORE_CONTAINER   1 = leave the container running for inspection
# =====================================================================
set -Eeuo pipefail

BACKUP_ROOT="${BACKUP_ROOT:-/var/backups/onlinestore}"
MSSQL_IMAGE="${MSSQL_IMAGE:-mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04}"
RESTORE_TEST_MSSQL_PID="${RESTORE_TEST_MSSQL_PID:-Express}"
KEEP_RESTORE_CONTAINER="${KEEP_RESTORE_CONTAINER:-0}"
SQLCMD="/opt/mssql-tools18/bin/sqlcmd"
CONTAINER="onlinestore-restore-test-$$"
WORK_DIR=""

log() { printf '[%s] %s\n' "$(date -u +%H:%M:%S)" "$*"; }
die() { echo "RESTORE TEST FAILED: $*" >&2; exit 1; }
require() { command -v "$1" >/dev/null 2>&1 || die "Missing command: $1"; }

cleanup() {
  if [[ "$KEEP_RESTORE_CONTAINER" != "1" ]]; then
    docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
  else
    log "Container $CONTAINER kept (KEEP_RESTORE_CONTAINER=1); remove it with: docker rm -f $CONTAINER"
  fi
  if [[ -n "$WORK_DIR" && -d "$WORK_DIR" ]]; then rm -rf "$WORK_DIR"; fi
}
trap cleanup EXIT

manifest_value() {
  grep -E "^$1=" "$BACKUP_DIR/manifest.txt" | head -n 1 | cut -d= -f2-
}

sql() {
  docker exec "$CONTAINER" "$SQLCMD" -S localhost -U sa -C -b -h -1 -W -Q "SET NOCOUNT ON; $1"
}

require docker
require sha256sum
require tar

# ---------------------------------------------------------------------
# 0. Which backup
# ---------------------------------------------------------------------
if [[ $# -ge 1 ]]; then
  BACKUP_DIR="$1"
else
  BACKUP_DIR="$(find "$BACKUP_ROOT" -mindepth 1 -maxdepth 1 -type d \
    -regextype posix-extended -regex '.*/[0-9]{8}_[0-9]{6}' | sort | tail -n 1)"
fi
[[ -n "$BACKUP_DIR" && -d "$BACKUP_DIR" ]] || die "No backup directory found (argument or $BACKUP_ROOT)"
[[ -f "$BACKUP_DIR/manifest.txt" ]] || die "manifest.txt missing in $BACKUP_DIR"

DB_NAME="$(manifest_value database)"
BAK_NAME="$(manifest_value database_backup)"
UPLOADS_NAME="$(manifest_value uploads_archive)"
EXPECTED_MIGRATION="$(manifest_value last_migration)"
EXPECTED_UPLOAD_FILES="$(manifest_value uploads_files)"
[[ "$DB_NAME" =~ ^[A-Za-z0-9_]+$ ]] || die "Unexpected database name in manifest: $DB_NAME"

log "Testing backup $BACKUP_DIR (database $DB_NAME)"

# ---------------------------------------------------------------------
# 1. Checksums
# ---------------------------------------------------------------------
log "Checking SHA256SUMS"
(cd "$BACKUP_DIR" && sha256sum --check --quiet SHA256SUMS) || die "checksum mismatch"

# ---------------------------------------------------------------------
# 2. Database restore into a throw-away container
# ---------------------------------------------------------------------
# A one-off random sa password; it only exists in this process and the
# container's environment (passed by name, never on the command line).
MSSQL_SA_PASSWORD="Rt1-$(head -c 16 /dev/urandom | od -An -tx1 | tr -d ' \n')"
SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"
export MSSQL_SA_PASSWORD SQLCMDPASSWORD

log "Starting throw-away SQL Server container $CONTAINER ($RESTORE_TEST_MSSQL_PID, no network)"
docker run -d --name "$CONTAINER" --network none --memory 2g \
  -e ACCEPT_EULA=Y \
  -e MSSQL_PID="$RESTORE_TEST_MSSQL_PID" \
  -e MSSQL_SA_PASSWORD \
  -e SQLCMDPASSWORD \
  "$MSSQL_IMAGE" >/dev/null

log "Waiting for SQL Server"
ready=0
for _ in $(seq 1 60); do
  if docker exec "$CONTAINER" "$SQLCMD" -S localhost -U sa -C -b -l 5 -Q "SELECT 1" >/dev/null 2>&1; then
    ready=1
    break
  fi
  sleep 3
done
((ready == 1)) || die "throw-away SQL Server did not start"

log "Copying $BAK_NAME into the container"
docker exec "$CONTAINER" mkdir -p /var/opt/mssql/restore
docker cp "$BACKUP_DIR/$BAK_NAME" "$CONTAINER:/var/opt/mssql/restore/$BAK_NAME"

# The throw-away instance has no database of this name and the default data
# directory is the same as in production, so no MOVE clause is needed.
log "Restoring $DB_NAME"
started=$(date +%s)
sql "RESTORE DATABASE [$DB_NAME] FROM DISK = N'/var/opt/mssql/restore/$BAK_NAME' WITH CHECKSUM, RECOVERY, STATS = 25;" >/dev/null
restore_seconds=$(( $(date +%s) - started ))

log "Running DBCC CHECKDB"
sql "DBCC CHECKDB (N'$DB_NAME') WITH NO_INFOMSGS, ALL_ERRORMSGS;" || die "DBCC CHECKDB reported errors"

ACTUAL_MIGRATION="$(sql "SELECT ISNULL(MAX(MigrationId), N'(none)') FROM [$DB_NAME].dbo.__EFMigrationsHistory;" | head -n 1)"
TABLES="$(sql "SELECT COUNT(*) FROM [$DB_NAME].sys.tables WHERE is_ms_shipped = 0;" | head -n 1)"
ROWS="$(sql "SELECT ISNULL(SUM(p.rows), 0) FROM [$DB_NAME].sys.partitions p JOIN [$DB_NAME].sys.tables t ON t.object_id = p.object_id WHERE p.index_id IN (0, 1) AND t.is_ms_shipped = 0;" | head -n 1)"

[[ "$ACTUAL_MIGRATION" == "$EXPECTED_MIGRATION" ]] \
  || die "last migration is '$ACTUAL_MIGRATION', manifest says '$EXPECTED_MIGRATION'"

# ---------------------------------------------------------------------
# 3. Uploads archive
# ---------------------------------------------------------------------
WORK_DIR="$(mktemp -d)"
log "Unpacking $UPLOADS_NAME into $WORK_DIR"
tar -xzf "$BACKUP_DIR/$UPLOADS_NAME" -C "$WORK_DIR"
ACTUAL_UPLOAD_FILES="$(find "$WORK_DIR" -type f | wc -l | tr -d ' ')"
[[ "$ACTUAL_UPLOAD_FILES" == "$EXPECTED_UPLOAD_FILES" ]] \
  || die "uploads archive holds $ACTUAL_UPLOAD_FILES files, manifest says $EXPECTED_UPLOAD_FILES"

# ---------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------
echo
echo "RESTORE TEST PASSED"
echo "  backup:           $BACKUP_DIR"
echo "  database:         $DB_NAME (restore ${restore_seconds}s, DBCC CHECKDB clean)"
echo "  last migration:   $ACTUAL_MIGRATION"
echo "  user tables:      $TABLES"
echo "  rows (approx.):   $ROWS"
echo "  upload files:     $ACTUAL_UPLOAD_FILES"
echo "  tested at (UTC):  $(date -u +%Y-%m-%dT%H:%M:%SZ)"
