#!/usr/bin/env bash
# One-shot, root-run recovery helper for a guest whose SSH service cannot stay reachable.
# It captures only numeric/process/service evidence and never reads credentials, command lines,
# environments, prompts, broker payloads, or market-data contents.
set -Eeuo pipefail

# The production recovery invocation supplies no positional metadata-script arguments.
bucket_name="${1:-${AI_TRADER_FORENSICS_BUCKET:-backup-bucket-e17fa12}}"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
root="/var/lib/ai-trader/forensics/recovery-${timestamp}"
archive="/var/lib/ai-trader/forensics/recovery-${timestamp}.tar.gz"
marker="/var/lib/ai-trader/forensics/legacy-backup-recovered"
legacy_cron="/etc/cron.d/ai-trader-backup"
legacy_script="/opt/ai-trader/bin/backup-db.sh"

umask 0077
[[ ! -e "$marker" ]] || exit 0
install -d -m 0700 "$root"

copy_if_present() {
    local source="$1"
    local destination="$2"
    [[ -e "$source" ]] || return
    install -d -m 0700 "$(dirname "$destination")"
    cp -a -- "$source" "$destination"
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

copy_if_present /var/lib/ai-trader/diagnostics "$root/diagnostics"
copy_if_present /var/lib/ai-trader/health "$root/health"
copy_if_present /etc/cron.d "$root/etc/cron.d"
copy_if_present /opt/ai-trader/bin/backup-db.sh "$root/backup-db.sh"
copy_if_present /var/log/ai-trader/backup-db.log "$root/backup-db.log"
copy_if_present /etc/systemd/system/ai-trader.service "$root/ai-trader.service"

crontab -l -u ai-trader > "$root/ai-trader.crontab" 2>/dev/null || true
systemctl list-timers --all --no-pager > "$root/systemd-timers.txt" 2>/dev/null || true
systemctl show ai-trader.service --no-pager \
    -p MainPID -p ControlGroup -p Result -p ExecMainCode -p ExecMainStatus \
    > "$root/ai-trader-service-state.txt" 2>/dev/null || true
ps -eo pid,ppid,user,comm,rss --sort=-rss > "$root/process-census.txt" 2>/dev/null || true
journalctl -k --since '2026-07-14 00:00:00 UTC' --no-pager \
    | grep -Ei 'out of memory|oom-kill|killed process' \
    > "$root/kernel-oom.txt" || true
dpkg-query -W -f='${binary:Package}\t${Version}\n' 'google-cloud*' 'snapd*' \
    > "$root/package-state.txt" 2>/dev/null || true

find "$root" -type f -printf '%P\t%s\n' | sort > "$root/manifest.txt"
tar -C "$(dirname "$root")" -czf "$archive" "$(basename "$root")"
sha256sum "$archive" > "${archive}.sha256"

if command -v gcloud >/dev/null 2>&1; then
    gcloud storage cp "$archive" "${archive}.sha256" \
        "gs://${bucket_name}/market-data/forensics/${timestamp}/" --quiet || true
fi

mapfile -t pids < <(legacy_backup_pids)
if ((${#pids[@]} > 0)); then
    kill -TERM "${pids[@]}" 2>/dev/null || true
    sleep 3
    mapfile -t pids < <(legacy_backup_pids)
    ((${#pids[@]} == 0)) || kill -KILL "${pids[@]}" 2>/dev/null || true
fi

rm -f -- "$legacy_cron" "$legacy_script"
systemctl restart ssh.service || true
touch "$marker"
printf 'forensic archive created at %s; legacy backup paths removed\n' "$archive"
