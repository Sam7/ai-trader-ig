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
EXIT_EVIDENCE_FILE="$DEPLOY_DIR/capture-worker-exit-evidence.sh"
WORKER_PACKAGE="$DEPLOY_DIR/ai-trader-worker.tar.gz"

fail() {
    echo "install-vm: $*" >&2
    exit 1
}

[[ -n "$BACKUP_BUCKET_NAME" ]] || fail "backup bucket name is required"
[[ -f "$SERVICE_FILE" ]] || fail "missing $SERVICE_FILE"
[[ -f "$EXIT_EVIDENCE_FILE" ]] || fail "missing $EXIT_EVIDENCE_FILE"
[[ -f "$WORKER_PACKAGE" ]] || fail "missing $WORKER_PACKAGE"

export DEBIAN_FRONTEND=noninteractive
export CLOUDSDK_SKIP_PY_COMPILATION=1

apt-get update
apt-get install -y ca-certificates gzip sqlite3 tar

if ! id -u ai-trader >/dev/null 2>&1; then
    useradd --system --home-dir "$DATA_DIR" --create-home --shell /usr/sbin/nologin ai-trader
fi

install -d -o root -g root -m 0755 "$APP_ROOT" "$BIN_DIR"
install -d -o ai-trader -g ai-trader -m 0750 "$APP_DIR" "$DATA_DIR" "$LOG_DIR"
install -d -o ai-trader -g ai-trader -m 0750 \
    "$DATA_DIR/market-data" \
    "$DATA_DIR/snapshot-publisher" \
    "$DATA_DIR/observability" \
    "$DATA_DIR/health" \
    "$DATA_DIR/diagnostics"
install -d -o root -g ai-trader -m 0750 /etc/ai-trader
touch /etc/ai-trader/ai-trader.env
chown root:ai-trader /etc/ai-trader/ai-trader.env
chmod 0640 /etc/ai-trader/ai-trader.env

find "$APP_DIR" -mindepth 1 -maxdepth 1 -exec rm -rf {} +
tar -xzf "$WORKER_PACKAGE" -C "$APP_DIR"
chown -R ai-trader:ai-trader "$APP_DIR"
chmod 0750 "$APP_DIR"
chmod 0750 "$APP_DIR/Trading.Worker"

install -o root -g root -m 0755 "$EXIT_EVIDENCE_FILE" "$BIN_DIR/capture-worker-exit-evidence.sh"

sed "s#REPLACE_WITH_BACKUP_BUCKET#$BACKUP_BUCKET_NAME#g" "$SERVICE_FILE" \
    > /etc/systemd/system/ai-trader.service
chmod 0644 /etc/systemd/system/ai-trader.service

systemctl daemon-reload
systemctl enable ai-trader.service
systemctl reset-failed ai-trader.service
systemctl restart ai-trader.service

systemctl --no-pager --full status ai-trader.service
