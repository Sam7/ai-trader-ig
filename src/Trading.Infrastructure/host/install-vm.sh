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

legacy_backup_pids() {
    local -a pending=()
    local -A seen=()
    local pid child

    while IFS= read -r pid; do
        [[ "$pid" =~ ^[0-9]+$ ]] && pending+=("$pid")
    done < <(pgrep -f '[b]ackup-db\.sh' || true)

    while ((${#pending[@]} > 0)); do
        pid="${pending[0]}"
        pending=("${pending[@]:1}")
        [[ -n "${seen[$pid]:-}" ]] && continue
        seen["$pid"]=1

        while IFS= read -r child; do
            [[ "$child" =~ ^[0-9]+$ ]] && pending+=("$child")
        done < <(pgrep -P "$pid" || true)
    done

    if ((${#seen[@]} > 0)); then
        printf '%s\n' "${!seen[@]}"
    fi
}

stop_legacy_backup_process_tree() {
    local -a pids=()
    local attempt

    mapfile -t pids < <(legacy_backup_pids)
    ((${#pids[@]} == 0)) && return

    kill -TERM "${pids[@]}" 2>/dev/null || true
    for attempt in 1 2 3; do
        sleep 1
        mapfile -t pids < <(legacy_backup_pids)
        ((${#pids[@]} == 0)) && return
    done

    kill -KILL "${pids[@]}" 2>/dev/null || true
}

assert_no_legacy_backup_artifacts() {
    local cron_file="${1:-${AI_TRADER_LEGACY_CRON_FILE:-/etc/cron.d/ai-trader-backup}}"
    local backup_script="${2:-${AI_TRADER_LEGACY_BACKUP_SCRIPT:-/opt/ai-trader/bin/backup-db.sh}}"

    [[ ! -e "$cron_file" ]] || fail "legacy backup cron is still present"
    [[ ! -e "$backup_script" ]] || fail "legacy backup script is still present"
    pgrep -f '[b]ackup-db\.sh' >/dev/null && fail "legacy backup process is still present"
    if id -u ai-trader >/dev/null 2>&1 && pgrep -u ai-trader -x gcloud >/dev/null; then
        fail "an ai-trader gcloud process remains after legacy backup cleanup"
    fi
}

remove_legacy_backup_artifacts() {
    local cron_file="${AI_TRADER_LEGACY_CRON_FILE:-/etc/cron.d/ai-trader-backup}"
    local backup_script="${AI_TRADER_LEGACY_BACKUP_SCRIPT:-/opt/ai-trader/bin/backup-db.sh}"

    stop_legacy_backup_process_tree
    rm -f -- "$cron_file" "$backup_script"
    assert_no_legacy_backup_artifacts "$cron_file" "$backup_script"
}

assert_worker_cgroup_is_stable() {
    local control_group cgroup_processes process_count attempt

    control_group="$(systemctl show ai-trader.service -p ControlGroup --value)"
    [[ "$control_group" == /* && "$control_group" != / ]] || fail "worker cgroup is unavailable"
    cgroup_processes="/sys/fs/cgroup$control_group/cgroup.procs"
    [[ -r "$cgroup_processes" ]] || fail "worker cgroup process list is unavailable"

    for attempt in 1 2 3; do
        systemctl is-active --quiet ai-trader.service || fail "worker service is not active"
        process_count="$(wc -l < "$cgroup_processes")"
        [[ "$process_count" == 1 ]] || fail "worker cgroup process count is $process_count, expected 1"
        assert_no_legacy_backup_artifacts
        if ((attempt < 3)); then
            sleep 2
        fi
    done
}

main() {
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

    # The July 2026 collector replaced this cron path. Older VMs keep it until
    # an installer explicitly removes the file, script, and any live descendants.
    remove_legacy_backup_artifacts

    systemctl daemon-reload
    systemctl enable ai-trader.service
    systemctl reset-failed ai-trader.service
    systemctl restart ai-trader.service
    assert_worker_cgroup_is_stable

    systemctl --no-pager --full status ai-trader.service
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
    main "$@"
fi
