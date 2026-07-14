#!/usr/bin/env bash
# Best-effort, local-only evidence captured by systemd after the worker exits.
# It deliberately makes no network call and always returns success.
set -u -o pipefail

umask 0077

diagnostics_dir="${AI_TRADER_DIAGNOSTICS_DIR:-/var/lib/ai-trader/diagnostics}"
cgroup_root="${AI_TRADER_CGROUP_ROOT:-/sys/fs/cgroup}"
proc_self_cgroup_path="${AI_TRADER_PROC_SELF_CGROUP_PATH:-/proc/self/cgroup}"

sanitize() {
    local value="${1:-unknown}"
    value="${value//[^a-zA-Z0-9._-]/_}"
    printf '%s' "$value"
}

read_number() {
    local path="$1"
    local value
    if [[ ! -r "$path" ]]; then
        printf 'null'
        return
    fi

    value="$(tr -d '[:space:]' < "$path" 2>/dev/null || true)"
    if [[ "$value" =~ ^[0-9]+$ ]]; then
        printf '%s' "$value"
    else
        printf 'null'
    fi
}

read_key_number() {
    local path="$1"
    local key="$2"
    local value
    if [[ ! -r "$path" ]]; then
        printf 'null'
        return
    fi

    value="$(awk -v target="$key" '$1 == target { print $2; exit }' "$path" 2>/dev/null || true)"
    if [[ "$value" =~ ^[0-9]+$ ]]; then
        printf '%s' "$value"
    else
        printf 'null'
    fi
}

mkdir -p "$diagnostics_dir" 2>/dev/null || exit 0
tmp_path="$(mktemp "$diagnostics_dir/.exit-evidence.XXXXXX" 2>/dev/null || true)"
[[ -n "$tmp_path" ]] || exit 0

cgroup_relative_path="$(awk -F: '$2 == "" { print $3; exit }' "$proc_self_cgroup_path" 2>/dev/null || true)"
if [[ -n "$cgroup_relative_path" && "$cgroup_relative_path" != "/" ]]; then
    cgroup_directory="$cgroup_root$cgroup_relative_path"
else
    cgroup_directory="$cgroup_root"
fi

timestamp="$(date -u +%Y%m%dT%H%M%S.%NZ)"
final_path="$diagnostics_dir/exit-${timestamp}-${BASHPID}-${RANDOM}.json"

if ! {
    printf '{\n'
    printf '  "schemaVersion": 1,\n'
    printf '  "observedAtUtc": "%s",\n' "$timestamp"
    printf '  "unit": "ai-trader.service",\n'
    printf '  "serviceResult": "%s",\n' "$(sanitize "${SERVICE_RESULT:-unknown}")"
    printf '  "exitCode": "%s",\n' "$(sanitize "${EXIT_CODE:-unknown}")"
    printf '  "exitStatus": "%s",\n' "$(sanitize "${EXIT_STATUS:-unknown}")"
    printf '  "mainPid": '
    read_number <(printf '%s\n' "${MAINPID:-0}")
    printf ',\n'
    printf '  "cgroup": {\n'
    printf '    "memoryCurrentBytes": '
    read_number "$cgroup_directory/memory.current"
    printf ',\n    "memoryPeakBytes": '
    read_number "$cgroup_directory/memory.peak"
    printf ',\n    "memoryEvents": {\n'
    printf '      "high": '
    read_key_number "$cgroup_directory/memory.events" high
    printf ',\n      "max": '
    read_key_number "$cgroup_directory/memory.events" max
    printf ',\n      "oom": '
    read_key_number "$cgroup_directory/memory.events" oom
    printf ',\n      "oomKill": '
    read_key_number "$cgroup_directory/memory.events" oom_kill
    printf '\n    },\n    "memoryStat": {\n'
    printf '      "anon": '
    read_key_number "$cgroup_directory/memory.stat" anon
    printf ',\n      "file": '
    read_key_number "$cgroup_directory/memory.stat" file
    printf ',\n      "kernelStack": '
    read_key_number "$cgroup_directory/memory.stat" kernel_stack
    printf ',\n      "slab": '
    read_key_number "$cgroup_directory/memory.stat" slab
    printf '\n    }\n  }\n}\n'
} > "$tmp_path"; then
    rm -f "$tmp_path"
    exit 0
fi

mv -f "$tmp_path" "$final_path" 2>/dev/null || rm -f "$tmp_path"
exit 0
