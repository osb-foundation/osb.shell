#!/usr/bin/env bash
# Golden-transcript acceptance run (A11 + F4): drives the CURRENT kernel ISO
# over serial with the same directives as the legacy reference and diffs the
# normalized output against cases/*.out.
#
# Usage: tests/golden/golden.sh [iso]     (default: ../osb.kernel/osb.iso)
#
# Gating (F4a): tests/golden/LEVEL is the level the shell has reached;
# MANIFEST maps each case to the minimum level that must pass it. Cases
# above LEVEL are skipped. Exit codes: 0 = all ran cases passed (skips are
# fine), 1 = mismatch or driver failure, 2 = usage/ISO error.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=lib.sh
source "$HERE/lib.sh"

ROOT="$(cd "$HERE/../.." && pwd)"
ISO="${1:-$ROOT/../osb.kernel/osb.iso}"
IMG="$(dirname "$ISO")/osb.img"
LEVEL="${GOLDEN_LEVEL:-$(cat "$HERE/LEVEL" 2>/dev/null || echo 1a.1)}"

[[ -f "$ISO" ]] || { echo "ERROR: ISO not found: $ISO (run ../osb.kernel/build.sh first)" >&2; exit 2; }
[[ -f "$IMG" ]] || { echo "ERROR: disk image not found: $IMG" >&2; exit 2; }

start=$SECONDS
pass=0 fail=0 skip=0
shopt -s nullglob
for case in "$HERE"/cases/*.in "$HERE"/cases/*.scm; do
    name="$(basename "$case")"
    name="${name%.*}"
    min="$(case_level "$name")"
    min="${min%% *}"
    if [[ -z "$min" ]]; then
        echo "WARN: $name has no MANIFEST entry - skipped" >&2
        skip=$((skip + 1))
        continue
    fi
    if (( $(level_idx "$min") > $(level_idx "$LEVEL") )); then
        echo "SKIP: $name (needs >= $min, at $LEVEL)"
        skip=$((skip + 1))
        continue
    fi

    echo "case $name ..."
    # Throwaway image copy per case (mutating cases must not leak).
    tmp="$(mktemp /tmp/osb-case-XXXXXX.raw)"
    tmpimg="$(mktemp /tmp/osb-img-XXXXXX.img)"
    cp "$IMG" "$tmpimg"
    if ! python3 "$HERE/drive.py" "$ISO" "$tmpimg" "$case" --out "$tmp"; then
        echo "FAIL: $name (driver error)"
        fail=$((fail + 1))
        rm -f "$tmp" "$tmpimg"
        continue
    fi
    rm -f "$tmpimg"
    normalize "$name" < "$tmp" > "$tmp.n"
    rm -f "$tmp"
    expected="$HERE/cases/$name.out"
    [[ -f "$expected" ]] || { echo "ERROR: missing golden $expected (run capture.sh)" >&2; exit 2; }
    exp="$(cat "$expected")"
    act="$(cat "$tmp.n")"
    rm -f "$tmp.n"
    if diff -u <(printf '%s\n' "$exp") <(printf '%s\n' "$act") > "$HERE/cases/$name.diff"; then
        rm -f "$HERE/cases/$name.diff"
        echo "PASS: $name"
        pass=$((pass + 1))
    else
        echo "FAIL: $name (diff in cases/$name.diff)"
        fail=$((fail + 1))
    fi
done

echo "runtime: $((SECONDS - start))s | pass=$pass fail=$fail skip=$skip"
if [[ $fail -gt 0 ]]; then exit 1; fi
exit 0