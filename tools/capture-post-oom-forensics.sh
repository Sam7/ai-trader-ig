#!/usr/bin/env bash
# One-shot startup-script helper for a post-recovery incident. It preserves evidence and repairs SSH only.
set -Eeuo pipefail

umask 0077

bucket_name="${1:-${AI_TRADER_FORENSICS_BUCKET:-backup-bucket-e17fa12}}"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
root="/var/lib/ai-trader/forensics/post-oom-${timestamp}"
archive="/var/lib/ai-trader/forensics/post-oom-${timestamp}.tar.gz"
marker="/var/lib/ai-trader/forensics/post-oom-captured"

[[ ! -e "$marker" ]] || exit 0
mkdir -p "$root"

copy_if_present() {
    local source="$1"
    local destination="$2"
    [[ -e "$source" ]] || return 0
    mkdir -p "$(dirname "$destination")"
    cp -a "$source" "$destination"
}

copy_if_present /var/lib/ai-trader/diagnostics "$root/diagnostics"
copy_if_present /var/lib/ai-trader/health "$root/health"
copy_if_present /etc/cron.d "$root/etc/cron.d"
copy_if_present /opt/ai-trader/bin/backup-db.sh "$root/backup-db.sh"
# Preserve only timestamp/category evidence from the journal; never copy application log payloads.
journalctl -u ai-trader.service -k --since '2026-07-16 07:45:00 UTC' --no-pager 2>/dev/null |
    while IFS= read -r line; do
        category=""
        case "$line" in
            *"System.OutOfMemoryException"*) category="managed-oom" ;;
            *"Out of memory"*|*"oom-kill"*|*"Killed process"*) category="kernel-oom" ;;
            *"Started AI Trader"*|*"Stopped AI Trader"*|*"ai-trader.service: Failed"*) category="service-state" ;;
        esac
        [[ -z "$category" ]] || printf '%s %s\n' "${line:0:32}" "$category"
    done > "$root/journal-categories.txt" || true
ps -eo pid=,ppid=,user=,comm=,rss= --sort=-rss > "$root/process-census.txt" 2>/dev/null || true
systemctl show ai-trader.service -p ActiveState -p SubState -p Result -p ExecMainCode -p ExecMainStatus -p MainPID > "$root/service-state.txt" 2>/dev/null || true

tar -C "$(dirname "$root")" -czf "$archive" "$(basename "$root")"
sha256sum "$archive" > "$archive.sha256"
gcloud storage cp "$archive" "gs://${bucket_name}/market-data/forensics/${timestamp}/" || true
gcloud storage cp "$archive.sha256" "gs://${bucket_name}/market-data/forensics/${timestamp}/" || true

systemctl restart ssh.service || true
touch "$marker"
