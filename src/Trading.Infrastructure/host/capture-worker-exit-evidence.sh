#!/usr/bin/env bash
# Best-effort, local-only evidence captured by systemd after the worker exits.
# It deliberately makes no network call, command-line read, or environment read.
set -u -o pipefail

umask 0077

diagnostics_dir="${AI_TRADER_DIAGNOSTICS_DIR:-/var/lib/ai-trader/diagnostics}"
cgroup_root="${AI_TRADER_CGROUP_ROOT:-/sys/fs/cgroup}"
proc_self_cgroup_path="${AI_TRADER_PROC_SELF_CGROUP_PATH:-/proc/self/cgroup}"
proc_root="${AI_TRADER_PROC_ROOT:-/proc}"

sanitize() {
    local value="${1:-unknown}"
    value="${value//[^a-zA-Z0-9._-]/_}"
    printf '%s' "$value"
}

sanitize_cgroup() {
    local value="${1:-unknown}"
    value="${value//[^a-zA-Z0-9._\/-]/_}"
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

read_meminfo_bytes() {
    local key="$1"
    local kilobytes
    kilobytes="$(awk -v target="$key:" '$1 == target && $2 ~ /^[0-9]+$/ { print $2; exit }' "$proc_root/meminfo" 2>/dev/null || true)"
    if [[ "$kilobytes" =~ ^[0-9]+$ ]]; then
        printf '%s' "$((kilobytes * 1024))"
    else
        printf 'null'
    fi
}

write_number_map() {
    local path="$1"
    local first=true
    local key value _
    printf '{'
    if [[ -r "$path" ]]; then
        while read -r key value _; do
            [[ "$key" =~ ^[a-zA-Z0-9_.-]+$ && "$value" =~ ^[0-9]+$ ]] || continue
            if [[ "$first" == true ]]; then
                first=false
            else
                printf ','
            fi
            printf '"%s":%s' "$(sanitize "$key")" "$value"
        done < "$path"
    fi
    printf '}'
}

write_psi() {
    local path="$1"
    local first=true
    local category field key value
    printf '{'
    if [[ -r "$path" ]]; then
        while read -r category field; do
            [[ "$category" == "some" || "$category" == "full" ]] || continue
            for field in $field; do
                key="${field%%=*}"
                value="${field#*=}"
                [[ "$key" =~ ^(avg10|avg60|avg300|total)$ && "$value" =~ ^[0-9.]+$ ]] || continue
                if [[ "$first" == true ]]; then
                    first=false
                else
                    printf ','
                fi
                printf '"%s_%s":%s' "$category" "$key" "$value"
            done
        done < "$path"
    fi
    printf '}'
}

count_processes() {
    local path name count=0
    for path in "$proc_root"/*; do
        name="${path##*/}"
        [[ "$name" =~ ^[0-9]+$ ]] && count=$((count + 1))
    done
    printf '%s' "$count"
}

write_top_processes() {
    local path pid rss ppid uid executable cgroup first=true
    printf '['
    while IFS=$'\t' read -r pid rss ppid uid executable cgroup; do
        [[ "$pid" =~ ^[0-9]+$ ]] || continue
        if [[ "$first" == true ]]; then
            first=false
        else
            printf ','
        fi
        printf '{"pid":%s,"ppid":%s,"uid":%s,"executable":"%s","cgroup":"%s","rssBytes":%s}' \
            "$pid" "$ppid" "$uid" "$(sanitize "$executable")" "$(sanitize_cgroup "$cgroup")" "$((rss * 1024))"
    done < <(
        for path in "$proc_root"/*; do
            pid="${path##*/}"
            [[ "$pid" =~ ^[0-9]+$ && -r "$path/status" ]] || continue
            rss="$(awk '$1 == "VmRSS:" && $2 ~ /^[0-9]+$/ { print $2; exit }' "$path/status" 2>/dev/null || true)"
            ppid="$(awk '$1 == "PPid:" && $2 ~ /^[0-9]+$/ { print $2; exit }' "$path/status" 2>/dev/null || true)"
            uid="$(awk '$1 == "Uid:" && $2 ~ /^[0-9]+$/ { print $2; exit }' "$path/status" 2>/dev/null || true)"
            executable="$(head -n 1 "$path/comm" 2>/dev/null || true)"
            cgroup="$(awk -F: '$2 == "" { print $3; exit }' "$path/cgroup" 2>/dev/null || true)"
            [[ "$rss" =~ ^[0-9]+$ ]] || rss=0
            [[ "$ppid" =~ ^[0-9]+$ ]] || ppid=0
            [[ "$uid" =~ ^[0-9]+$ ]] || uid=0
            printf '%s\t%s\t%s\t%s\t%s\t%s\n' "$pid" "$rss" "$ppid" "$uid" "$executable" "$cgroup"
        done | sort -t $'\t' -k2,2nr -k1,1n | head -n 15
    )
    printf ']'
}

write_latest_artifacts() {
    local path first=true
    printf '['
    while read -r path; do
        [[ -n "$path" ]] || continue
        if [[ "$first" == true ]]; then
            first=false
        else
            printf ','
        fi
        printf '"%s"' "$(sanitize "${path##*/}")"
    done < <(find "$diagnostics_dir" -maxdepth 1 -type f \( -name '*.jsonl' -o -name 'forensic-*.gz' \) -printf '%f\n' 2>/dev/null | sort | tail -n 8)
    printf ']'
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
    printf '  "schemaVersion": 2,\n'
    printf '  "observedAtUtc": "%s",\n' "$timestamp"
    printf '  "unit": "ai-trader.service",\n'
    printf '  "serviceResult": "%s",\n' "$(sanitize "${SERVICE_RESULT:-unknown}")"
    printf '  "exitCode": "%s",\n' "$(sanitize "${EXIT_CODE:-unknown}")"
    printf '  "exitStatus": "%s",\n' "$(sanitize "${EXIT_STATUS:-unknown}")"
    printf '  "mainPid": '
    read_number <(printf '%s\n' "${MAINPID:-0}")
    printf ',\n  "cgroup": {\n'
    printf '    "memoryCurrentBytes": '
    read_number "$cgroup_directory/memory.current"
    printf ',\n    "memoryPeakBytes": '
    read_number "$cgroup_directory/memory.peak"
    printf ',\n    "memoryStat": '
    write_number_map "$cgroup_directory/memory.stat"
    printf ',\n    "memoryEvents": '
    write_number_map "$cgroup_directory/memory.events"
    printf ',\n    "memoryPressure": '
    write_psi "$cgroup_directory/memory.pressure"
    printf '\n  },\n  "host": {\n'
    printf '    "totalBytes": '; read_meminfo_bytes MemTotal
    printf ',\n    "availableBytes": '; read_meminfo_bytes MemAvailable
    printf ',\n    "cachedBytes": '; read_meminfo_bytes Cached
    printf ',\n    "dirtyBytes": '; read_meminfo_bytes Dirty
    printf ',\n    "slabBytes": '; read_meminfo_bytes Slab
    printf ',\n    "swapTotalBytes": '; read_meminfo_bytes SwapTotal
    printf ',\n    "swapFreeBytes": '; read_meminfo_bytes SwapFree
    printf ',\n    "memoryPressure": '; write_psi "$proc_root/pressure/memory"
    printf ',\n    "processCount": %s,\n' "$(count_processes)"
    printf '    "topProcesses": '; write_top_processes
    printf '\n  },\n  "latestArtifacts": '; write_latest_artifacts
    printf '\n}\n'
} > "$tmp_path"; then
    rm -f "$tmp_path"
    exit 0
fi

mv -f "$tmp_path" "$final_path" 2>/dev/null || rm -f "$tmp_path"
exit 0
