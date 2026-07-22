#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
installer="$repo_root/src/Trading.Infrastructure/host/install-vm.sh"
test_root="$(mktemp -d)"

cleanup() {
    rm -rf "$test_root"
}
trap cleanup EXIT

legacy_cron="$test_root/etc/cron.d/ai-trader-backup"
legacy_script="$test_root/opt/ai-trader/bin/backup-db.sh"
mkdir -p "$(dirname "$legacy_cron")" "$(dirname "$legacy_script")"
printf '%s\n' '*/5 * * * * ai-trader /opt/ai-trader/bin/backup-db.sh' > "$legacy_cron"
printf '%s\n' '#!/usr/bin/env bash' > "$legacy_script"

# Package-manager races on fresh images must wait for unattended-upgrades
# rather than aborting an otherwise valid deployment.
grep -q 'apt-get -o DPkg::Lock::Timeout=300 update' "$installer"
grep -q 'apt-get -o DPkg::Lock::Timeout=300 install' "$installer"

# The package is staged and checkpointed before the live app is replaced. The
# cleanup trap must run only when the installer exits, not when its checkpoint
# helper returns.
grep -q 'create_deployment_checkpoint "\$staged_app_dir"' "$installer"
grep -q "trap 'rm -rf -- \"\$staged_app_dir\"' EXIT" "$installer"
! grep -q "trap 'rm -rf -- \"\$staged_app_dir\"' RETURN" "$installer"
grep -q 'cd "\$1"' "$installer"
grep -q '"\$CONTINUITY_DIR" \\' "$installer"

# The installer is sourced so this test exercises only the legacy-removal path;
# it never invokes apt, systemd, or any production filesystem path.
source "$installer"

backup_tree_running=true
declare -a killed=()

id() { return 1; }

pgrep() {
    if [[ "$1" == "-f" && "$backup_tree_running" == true ]]; then
        printf '%s\n' 101
        return
    fi

    if [[ "$1" == "-P" && "$2" == 101 && "$backup_tree_running" == true ]]; then
        printf '%s\n' 102
        return
    fi

    return 1
}

kill() {
    killed=("$@")
    backup_tree_running=false
}

AI_TRADER_LEGACY_CRON_FILE="$legacy_cron" \
AI_TRADER_LEGACY_BACKUP_SCRIPT="$legacy_script" \
remove_legacy_backup_artifacts

[[ ! -e "$legacy_cron" ]]
[[ ! -e "$legacy_script" ]]
[[ "${killed[0]}" == "-TERM" ]]
[[ " ${killed[*]} " == *" 101 "* ]]
[[ " ${killed[*]} " == *" 102 "* ]]

# A second deploy must remain safe after the obsolete files are gone.
AI_TRADER_LEGACY_CRON_FILE="$legacy_cron" \
AI_TRADER_LEGACY_BACKUP_SCRIPT="$legacy_script" \
remove_legacy_backup_artifacts

assert_no_legacy_backup_artifacts "$legacy_cron" "$legacy_script"

printf '%s\n' 'install-vm legacy-backup cleanup tests passed'
