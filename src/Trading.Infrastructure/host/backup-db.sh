#!/usr/bin/env bash
set -Eeuo pipefail

DB_PATH="${AI_TRADER_DB_PATH:-/var/lib/ai-trader/market-data/ig-market-data.sqlite}"
STAGING_DIR="${AI_TRADER_BACKUP_STAGING_DIR:-/var/lib/ai-trader/backups/staging}"
DESTINATION="${AI_TRADER_BACKUP_DESTINATION:-}"
SQLITE_BIN="${SQLITE_BIN:-sqlite3}"
GCLOUD_BIN="${GCLOUD_BIN:-gcloud}"
DRY_RUN="false"

usage() {
    cat <<'USAGE'
Usage: backup-db.sh [--db PATH] [--staging-dir PATH] [--destination gs://bucket/prefix] [--dry-run]

Creates a SQLite-consistent backup with the sqlite3 .backup command, verifies it,
then syncs the staging directory to Google Cloud Storage with gcloud storage rsync.
USAGE
}

fail() {
    echo "backup-db: $*" >&2
    exit 1
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --db)
            [[ $# -ge 2 ]] || fail "--db requires a path"
            DB_PATH="$2"
            shift 2
            ;;
        --staging-dir)
            [[ $# -ge 2 ]] || fail "--staging-dir requires a path"
            STAGING_DIR="$2"
            shift 2
            ;;
        --destination)
            [[ $# -ge 2 ]] || fail "--destination requires a gs:// URI"
            DESTINATION="$2"
            shift 2
            ;;
        --dry-run)
            DRY_RUN="true"
            shift
            ;;
        --help|-h)
            usage
            exit 0
            ;;
        *)
            fail "unknown argument '$1'"
            ;;
    esac
done

[[ -f "$DB_PATH" ]] || fail "database not found at '$DB_PATH'"
[[ "$DRY_RUN" == "true" || -n "$DESTINATION" ]] || fail "AI_TRADER_BACKUP_DESTINATION or --destination is required"

mkdir -p "$STAGING_DIR"

backup_name="$(basename "$DB_PATH")"
backup_path="$STAGING_DIR/$backup_name"
tmp_backup_path="$STAGING_DIR/.$backup_name.tmp.$$"
checksum_path="$backup_path.sha256"

cleanup() {
    rm -f "$tmp_backup_path"
}
trap cleanup EXIT

"$SQLITE_BIN" "$DB_PATH" ".timeout 10000" ".backup '$tmp_backup_path'"

quick_check="$("$SQLITE_BIN" "$tmp_backup_path" "PRAGMA quick_check;")"
[[ "$quick_check" == "ok" ]] || fail "backup quick_check failed: $quick_check"

mv -f "$tmp_backup_path" "$backup_path"
rm -f "$checksum_path"
sha256sum "$backup_path" | sed "s#  .*#  $backup_name#" > "$checksum_path"

if [[ "$DRY_RUN" == "true" ]]; then
    echo "backup-db: dry run created '$backup_path' and '$checksum_path'"
    if [[ -n "$DESTINATION" ]]; then
        echo "backup-db: would run '$GCLOUD_BIN storage rsync --recursive $STAGING_DIR $DESTINATION'"
    fi
    exit 0
fi

"$GCLOUD_BIN" storage rsync --recursive "$STAGING_DIR" "$DESTINATION"
