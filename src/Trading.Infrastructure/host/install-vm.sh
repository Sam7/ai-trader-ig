#!/usr/bin/env bash
set -Eeuo pipefail

BACKUP_BUCKET_NAME="${1:-}"
DEPLOY_DIR="${2:-/tmp/ai-trader-deploy}"

APP_ROOT="/opt/ai-trader"
APP_DIR="$APP_ROOT/app"
BIN_DIR="$APP_ROOT/bin"
DATA_DIR="/var/lib/ai-trader"
LOG_DIR="/var/log/ai-trader"
SERVICE_FILE="$DEPLOY_DIR/ai-trader.service"
CRON_FILE="$DEPLOY_DIR/ai-trader-backup.cron"
BACKUP_SCRIPT="$DEPLOY_DIR/backup-db.sh"
WORKER_PACKAGE="$DEPLOY_DIR/ai-trader-worker.tar.gz"

fail() {
    echo "install-vm: $*" >&2
    exit 1
}

[[ -n "$BACKUP_BUCKET_NAME" ]] || fail "backup bucket name is required"
[[ -f "$SERVICE_FILE" ]] || fail "missing $SERVICE_FILE"
[[ -f "$CRON_FILE" ]] || fail "missing $CRON_FILE"
[[ -f "$BACKUP_SCRIPT" ]] || fail "missing $BACKUP_SCRIPT"
[[ -f "$WORKER_PACKAGE" ]] || fail "missing $WORKER_PACKAGE"

export DEBIAN_FRONTEND=noninteractive
export CLOUDSDK_SKIP_PY_COMPILATION=1

apt-get update
apt-get install -y ca-certificates cron curl gnupg gzip sqlite3 tar

if ! command -v gcloud >/dev/null 2>&1; then
    install -d -m 0755 /usr/share/keyrings
    curl -fsSL https://packages.cloud.google.com/apt/doc/apt-key.gpg \
        | gpg --dearmor --yes -o /usr/share/keyrings/cloud.google.gpg
    echo "deb [signed-by=/usr/share/keyrings/cloud.google.gpg] https://packages.cloud.google.com/apt cloud-sdk main" \
        > /etc/apt/sources.list.d/google-cloud-sdk.list
    apt-get update
    apt-get install -y google-cloud-cli
fi

if ! id -u ai-trader >/dev/null 2>&1; then
    useradd --system --home-dir "$DATA_DIR" --create-home --shell /usr/sbin/nologin ai-trader
fi

install -d -o root -g root -m 0755 "$APP_ROOT" "$BIN_DIR"
install -d -o ai-trader -g ai-trader -m 0750 "$APP_DIR" "$DATA_DIR" "$LOG_DIR"
install -d -o ai-trader -g ai-trader -m 0750 \
    "$DATA_DIR/market-data" \
    "$DATA_DIR/backups/staging" \
    "$DATA_DIR/observability"

find "$APP_DIR" -mindepth 1 -maxdepth 1 -exec rm -rf {} +
tar -xzf "$WORKER_PACKAGE" -C "$APP_DIR"
chown -R ai-trader:ai-trader "$APP_DIR"
chmod 0750 "$APP_DIR"
chmod 0750 "$APP_DIR/Trading.Worker"

install -o root -g root -m 0755 "$BACKUP_SCRIPT" "$BIN_DIR/backup-db.sh"
install -o root -g root -m 0644 "$SERVICE_FILE" /etc/systemd/system/ai-trader.service
sed "s#gs://REPLACE_WITH_BACKUP_BUCKET/market-data#gs://$BACKUP_BUCKET_NAME/market-data#g" "$CRON_FILE" \
    > /etc/cron.d/ai-trader-backup
chmod 0644 /etc/cron.d/ai-trader-backup

systemctl daemon-reload
systemctl enable cron
systemctl enable ai-trader.service
systemctl restart ai-trader.service
systemctl restart cron

systemctl --no-pager --full status ai-trader.service
