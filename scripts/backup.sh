#!/usr/bin/env bash
# =====================================================================
# Batch 52 (OPS-01, OPS-05, N19): backup of the production database and
# the uploads volume. Run on the Docker host (cron, see docs/runbook.md).
#
#   scripts/backup.sh                 # writes $BACKUP_ROOT/<UTC stamp>/
#
# Produces in one directory:
#   ShopDB_<stamp>.bak        full SQL Server backup (WITH CHECKSUM, verified)
#   uploads_<stamp>.tar.gz    the uploads volume (product images)
#   manifest.txt              what was backed up (edition, last migration, sizes)
#   SHA256SUMS                checksums of the two files
#
# A backup is only useful once it has been restored: run
# scripts/restore-test.sh against it (docs/runbook.md, "Restore test").
#
# Environment (all optional):
#   BACKUP_ROOT              default /var/backups/onlinestore
#   BACKUP_RETENTION_DAYS    default 14 (older stamp directories are deleted)
#   SQL_CONTAINER            default onlinestore-sqlserver
#   DB_NAME                  default ShopDB
#   UPLOADS_VOLUME           default onlinestoreapi_uploads_data
#   HELPER_IMAGE             default alpine:3.20 (only used to read the volume)
#
# The SQL container must have SQLCMDPASSWORD set (docker-compose.prod.yml
# does this), so no password appears on a command line.
# =====================================================================
set -Eeuo pipefail

BACKUP_ROOT="${BACKUP_ROOT:-/var/backups/onlinestore}"
BACKUP_RETENTION_DAYS="${BACKUP_RETENTION_DAYS:-14}"
SQL_CONTAINER="${SQL_CONTAINER:-onlinestore-sqlserver}"
DB_NAME="${DB_NAME:-ShopDB}"
UPLOADS_VOLUME="${UPLOADS_VOLUME:-onlinestoreapi_uploads_data}"
HELPER_IMAGE="${HELPER_IMAGE:-alpine:3.20}"
SQLCMD="/opt/mssql-tools18/bin/sqlcmd"
CONTAINER_BACKUP_DIR="/var/opt/mssql/backup"

STAMP="$(date -u +%Y%m%d_%H%M%S)"
TARGET="$BACKUP_ROOT/$STAMP"
BAK_NAME="${DB_NAME}_${STAMP}.bak"
UPLOADS_NAME="uploads_${STAMP}.tar.gz"

log() { printf '[%s] %s\n' "$(date -u +%H:%M:%S)" "$*"; }
die() { echo "ERROR: $*" >&2; exit 1; }
require() { command -v "$1" >/dev/null 2>&1 || die "Missing command: $1"; }

sql() {
  docker exec "$SQL_CONTAINER" "$SQLCMD" -S localhost -U sa -C -b -h -1 -W -Q "SET NOCOUNT ON; $1"
}

cleanup_container_file() {
  docker exec "$SQL_CONTAINER" rm -f "$CONTAINER_BACKUP_DIR/$BAK_NAME" >/dev/null 2>&1 || true
}

require docker
require sha256sum
[[ "$BACKUP_RETENTION_DAYS" =~ ^[0-9]+$ ]] || die "BACKUP_RETENTION_DAYS must be a number"
[[ "$DB_NAME" =~ ^[A-Za-z0-9_]+$ ]] || die "DB_NAME may contain only letters, digits and _"

docker inspect "$SQL_CONTAINER" >/dev/null 2>&1 || die "SQL container not found: $SQL_CONTAINER"
docker volume inspect "$UPLOADS_VOLUME" >/dev/null 2>&1 || die "Uploads volume not found: $UPLOADS_VOLUME"

umask 077
mkdir -p "$TARGET"
trap cleanup_container_file EXIT

# ---------------------------------------------------------------------
# 1. Database
# ---------------------------------------------------------------------
log "Backing up database $DB_NAME"
docker exec "$SQL_CONTAINER" mkdir -p "$CONTAINER_BACKUP_DIR"
# Express does not support backup compression, so none is requested.
sql "BACKUP DATABASE [$DB_NAME] TO DISK = N'$CONTAINER_BACKUP_DIR/$BAK_NAME' WITH CHECKSUM, INIT, FORMAT, NAME = N'$DB_NAME full $STAMP';" >/dev/null

log "Verifying backup file inside the container"
sql "RESTORE VERIFYONLY FROM DISK = N'$CONTAINER_BACKUP_DIR/$BAK_NAME' WITH CHECKSUM;" >/dev/null

log "Copying backup to $TARGET"
docker cp "$SQL_CONTAINER:$CONTAINER_BACKUP_DIR/$BAK_NAME" "$TARGET/$BAK_NAME"
cleanup_container_file

EDITION="$(sql "SELECT CAST(SERVERPROPERTY('Edition') AS nvarchar(128));" | head -n 1)"
VERSION="$(sql "SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128));" | head -n 1)"
LAST_MIGRATION="$(sql "SELECT ISNULL(MAX(MigrationId), N'(none)') FROM [$DB_NAME].dbo.__EFMigrationsHistory;" | head -n 1)"
DB_SIZE_MB="$(sql "SELECT CAST(SUM(CAST(size AS bigint)) * 8 / 1024 AS varchar(20)) FROM [$DB_NAME].sys.database_files WHERE type = 0;" | head -n 1)"

# ---------------------------------------------------------------------
# 2. Uploads volume (read-only mount; the API keeps running)
# ---------------------------------------------------------------------
log "Archiving uploads volume $UPLOADS_VOLUME"
docker run --rm --network none \
  -v "$UPLOADS_VOLUME:/data:ro" \
  -v "$TARGET:/backup" \
  "$HELPER_IMAGE" \
  tar -czf "/backup/$UPLOADS_NAME" -C /data .
UPLOAD_FILES="$(tar -tzf "$TARGET/$UPLOADS_NAME" | grep -vc '/$' || true)"

# ---------------------------------------------------------------------
# 3. Manifest and checksums
# ---------------------------------------------------------------------
{
  echo "stamp=$STAMP"
  echo "database=$DB_NAME"
  echo "sql_server_edition=$EDITION"
  echo "sql_server_version=$VERSION"
  echo "last_migration=$LAST_MIGRATION"
  echo "data_file_size_mb=$DB_SIZE_MB"
  echo "database_backup=$BAK_NAME"
  echo "uploads_archive=$UPLOADS_NAME"
  echo "uploads_files=$UPLOAD_FILES"
} > "$TARGET/manifest.txt"

(cd "$TARGET" && sha256sum "$BAK_NAME" "$UPLOADS_NAME" > SHA256SUMS)

# Express edition: a single database may not grow beyond 10 GB (N15).
if [[ "$EDITION" == Express* && "$DB_SIZE_MB" =~ ^[0-9]+$ && "$DB_SIZE_MB" -ge 8192 ]]; then
  log "WARNING: $DB_NAME data file is ${DB_SIZE_MB} MB; the Express limit is 10240 MB. Plan the move to Standard."
fi

# ---------------------------------------------------------------------
# 4. Retention (only directories that look like our stamps)
# ---------------------------------------------------------------------
if [[ "$BACKUP_RETENTION_DAYS" -gt 0 ]]; then
  find "$BACKUP_ROOT" -mindepth 1 -maxdepth 1 -type d \
    -regextype posix-extended -regex '.*/[0-9]{8}_[0-9]{6}' \
    -mtime +"$BACKUP_RETENTION_DAYS" -print -exec rm -rf {} + || true
fi

log "Backup complete: $TARGET"
cat "$TARGET/manifest.txt"
echo
echo "Copy $TARGET off this machine (a backup on the same disk does not survive the disk)."
