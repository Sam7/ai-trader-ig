#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
script="$repo_root/src/Trading.Infrastructure/host/capture-worker-exit-evidence.sh"
test_root="$(mktemp -d)"

cleanup() {
    rm -rf "$test_root"
}
trap cleanup EXIT

diagnostics="$test_root/diagnostics"
cgroup="$test_root/cgroup"
proc_root="$test_root/proc"
mkdir -p "$diagnostics" "$cgroup" "$proc_root/100" "$proc_root/pressure"
printf '%s\n' '{"sequence":1}' > "$diagnostics/worker-test-0001.jsonl"
printf '%s\n' 'Name: Worker' > "$diagnostics/forensic-test.gz"
printf '%s\n' 'never-read-command-line-secret' > "$proc_root/100/cmdline"
printf '%s\n' 'Trading.Worker' > "$proc_root/100/comm"
printf '%s\n' $'Uid:\t1000\t1000\t1000\t1000\nPPid:\t1\nVmRSS:\t123 kB' > "$proc_root/100/status"
printf '%s\n' '0::/system.slice/ai-trader.service' > "$proc_root/100/cgroup"
printf '%s\n' $'MemTotal: 1024 kB\nMemAvailable: 512 kB\nCached: 128 kB\nDirty: 4 kB\nSlab: 8 kB\nSwapTotal: 0 kB\nSwapFree: 0 kB' > "$proc_root/meminfo"
printf '%s\n' 'some avg10=0.01 avg60=0.00 avg300=0.00 total=5' > "$proc_root/pressure/memory"
printf '%s\n' '268435456' > "$cgroup/memory.current"
printf '%s\n' '300000000' > "$cgroup/memory.peak"
printf '%s\n' $'anon 1024\nfile 32' > "$cgroup/memory.stat"
printf '%s\n' $'high 1\nmax 2\noom 0\noom_kill 0' > "$cgroup/memory.events"
printf '%s\n' 'some avg10=0.00 avg60=0.00 avg300=0.00 total=0' > "$cgroup/memory.pressure"

AI_TRADER_DIAGNOSTICS_DIR="$diagnostics" \
AI_TRADER_CGROUP_ROOT="$cgroup" \
AI_TRADER_PROC_SELF_CGROUP_PATH="$test_root/missing-self-cgroup" \
AI_TRADER_PROC_ROOT="$proc_root" \
SERVICE_RESULT=signal \
EXIT_CODE=killed \
EXIT_STATUS=9 \
MAINPID=100 \
bash "$script"

artifact="$(find "$diagnostics" -maxdepth 1 -name 'exit-*.json' -print -quit)"
[[ -n "$artifact" ]]
grep -q '"schemaVersion": 2' "$artifact"
grep -q '"host"' "$artifact"
grep -q '"processCount": 1' "$artifact"
grep -q '"latestArtifacts"' "$artifact"
grep -q '"memoryStat"' "$artifact"
grep -q '"memoryEvents"' "$artifact"
! grep -q 'never-read-command-line-secret' "$artifact"

printf '%s\n' 'capture-worker-exit-evidence tests passed'
