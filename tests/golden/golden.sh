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
#
# The ABI test programs (TSpawn/TExit/TArgs/TPrint/TWait) are gated behind
# WITH_TESTS=1 in ../osb.kernel/build.sh (review item 6), and the 6Ah cases
# (21-exit7 -> TWait.EXE) need them. So when this harness builds the image
# itself (no explicit ISO argument), it always builds with WITH_TESTS=1. To
# run against an already-built image that already contains the test programs,
# pass the ISO path explicitly: `golden.sh path/to/osb.iso`.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=lib.sh
source "$HERE/lib.sh"

ROOT="$(cd "$HERE/../.." && pwd)"
ISO="${1:-}"
BUILD=0
if [[ -z "$ISO" ]]; then
    # No ISO given: build a fresh one with the test programs staged (item 6).
    ISO="$ROOT/../osb.kernel/osb.iso"
    BUILD=1
fi
IMG="$(dirname "$ISO")/osb.img"
LEVEL="${GOLDEN_LEVEL:-$(cat "$HERE/LEVEL" 2>/dev/null || echo 1a.1)}"

if [[ "$BUILD" == "1" ]]; then
    echo "building kernel image (WITH_TESTS=1) ..."
    WITH_TESTS=1 "$ROOT/../osb.kernel/build.sh" >/dev/null
fi

[[ -f "$ISO" ]] || { echo "ERROR: ISO not found: $ISO (run ../osb.kernel/build.sh first)" >&2; exit 2; }
[[ -f "$IMG" ]] || { echo "ERROR: disk image not found: $IMG" >&2; exit 2; }

# Point A:\OSB\CONF\BOOT at <prog> on a throwaway image. debugfs refuses to
# overwrite an existing file, so remove it first, then write; reconcile the
# bitmaps afterwards (debugfs does not update them). Used by cases whose boot
# program is the test (21-exit7 -> TWait.EXE) rather than the shell.
patch_boot() {
    local img="$1" prog="$2" bf
    bf="$(mktemp)"
    printf '%s\n' "$prog" > "$bf"
    debugfs -w -R "rm /OSB/CONF/BOOT" "$img" >/dev/null 2>&1 || true
    if ! debugfs -w -R "write $bf /OSB/CONF/BOOT" "$img" >/dev/null 2>&1; then
        echo "ERROR: failed to patch A:\\OSB\\CONF\\BOOT on $img" >&2
        rm -f "$bf"
        return 1
    fi
    e2fsck -fy "$img" >/dev/null 2>&1 || true
    rm -f "$bf"
}

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
    # A `boot <prog>` directive overrides A:\OSB\CONF\BOOT for this case (the
    # 6Ah spec case boots TWait.EXE directly). Strip it from the script handed
    # to drive.py, which would otherwise reject it as an unknown directive.
    bootprog=""
    scrip="$case"
    if grep -q '^boot ' "$case"; then
        bootprog="$(sed -n 's/^boot //p' "$case" | head -1)"
        scrip="$(mktemp /tmp/osb-scm-XXXXXX.scm)"
        grep -v '^boot ' "$case" > "$scrip"
    fi
    # Throwaway image copy per case (mutating cases must not leak).
    tmp="$(mktemp /tmp/osb-case-XXXXXX.raw)"
    tmpimg="$(mktemp /tmp/osb-img-XXXXXX.img)"
    cp "$IMG" "$tmpimg"
    if [[ -n "$bootprog" ]] && ! patch_boot "$tmpimg" "$bootprog"; then
        echo "FAIL: $name (BOOT patch)"
        fail=$((fail + 1))
        rm -f "$tmp" "$tmpimg"
        [[ "$scrip" != "$case" ]] && rm -f "$scrip"
        continue
    fi
    if ! python3 "$HERE/drive.py" "$ISO" "$tmpimg" "$scrip" --out "$tmp"; then
        echo "FAIL: $name (driver error)"
        fail=$((fail + 1))
        rm -f "$tmp" "$tmpimg"
        [[ "$scrip" != "$case" ]] && rm -f "$scrip"
        continue
    fi
    [[ "$scrip" != "$case" ]] && rm -f "$scrip"
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