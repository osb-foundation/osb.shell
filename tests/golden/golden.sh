#!/usr/bin/env bash
# Golden-transcript acceptance run (A11): boots the CURRENT kernel ISO with
# the same serial input as the legacy reference and diffs the normalized
# output against cases/*.out.
#
# Usage: tests/golden/golden.sh [iso]     (default: ../osb.kernel/osb.iso)
#
# Exit codes: 0 = all cases pass (or skipped, see below), 1 = mismatch,
# 2 = usage/ISO error. When the shell under test has no serial input path
# yet (phase 1a: banner + idle), the run reports SKIP and exits 0 - the
# harness starts gating from the slice that adds GETCHAR.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=lib.sh
source "$HERE/lib.sh"

ROOT="$(cd "$HERE/../.." && pwd)"
ISO="${1:-$ROOT/../osb.kernel/osb.iso}"
IMG="$(dirname "$ISO")/osb.img"

[[ -f "$ISO" ]] || { echo "ERROR: ISO not found: $ISO (run ../osb.kernel/build.sh first)" >&2; exit 2; }
[[ -f "$IMG" ]] || { echo "ERROR: disk image not found: $IMG" >&2; exit 2; }

fails=0
ran=0
shopt -s nullglob
for inf in "$HERE"/cases/*.in; do
    name="$(basename "$inf" .in)"
    expected="$HERE/cases/$name.out"
    [[ -f "$expected" ]] || { echo "ERROR: missing golden $expected (run capture.sh)" >&2; exit 2; }

    echo "case $name ..."
    raw="$(run_case "$ISO" "$IMG" "$inf" || true)"

    # Phase 1a shell: banner + idle, no serial input path yet. Detect on the
    # raw transcript (normalize would strip everything - there is no login:).
    if grep -q "phase 1a" <<<"$raw" && ! grep -q "login:" <<<"$raw"; then
        echo "SKIP: $name (shell has no serial input yet - phase 1a; gating starts in 1b)"
        continue
    fi
    ran=$((ran + 1))

    actual="$(normalize <<<"$raw")"
    # Command substitution strips trailing newlines on both sides; compare
    # with exactly one so captures taken mid-prompt diff cleanly.
    exp="$(cat "$expected")"

    if diff -u <(printf '%s\n' "$exp") <(printf '%s\n' "$actual") > "$HERE/cases/$name.diff"; then
        rm -f "$HERE/cases/$name.diff"
        echo "PASS: $name"
    else
        echo "FAIL: $name (diff in $name.diff)"
        fails=$((fails + 1))
    fi
done

if [[ $ran -eq 0 ]]; then
    echo "All cases skipped (phase 1a shell). Harness ready for 1b."
    exit 0
fi
if [[ $fails -gt 0 ]]; then
    echo "$fails case(s) failed."
    exit 1
fi
echo "All $ran case(s) passed."