#!/usr/bin/env bash
# Golden-transcript acceptance run (A11 + F4): drives the CURRENT kernel ISO
# over serial with the same directives as the legacy reference and diffs the
# normalized output against cases/*.out (plus cases/*.vga when present).
#
# Usage: tests/golden/golden.sh [iso] [-j N]     (default iso: ../osb.kernel/osb.iso)
#   -j N   run N cases in parallel (default: nproc/2; 1 = sequential)
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
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=lib.sh
source "$HERE/lib.sh"

ROOT="$(cd "$HERE/../.." && pwd)"
ISO=""
JOBS="${OSB_JOBS:-}"
while [[ $# -gt 0 ]]; do
    case "$1" in
        -j|--jobs) JOBS="${2:?usage: golden.sh [iso] [-j N]}"; shift 2 ;;
        -*) echo "usage: golden.sh [iso] [-j N]" >&2; exit 2 ;;
        *) if [[ -z "$ISO" ]]; then
               ISO="$1"
           else
               echo "usage: golden.sh [iso] [-j N]" >&2
               exit 2
           fi
           shift ;;
    esac
done
BUILD=0
if [[ -z "$ISO" ]]; then
    # No ISO given: build a fresh one with the test programs staged (item 6).
    ISO="$ROOT/../osb.kernel/osb.iso"
    BUILD=1
fi
IMG="$(dirname "$ISO")/osb.img"
LEVEL="${GOLDEN_LEVEL:-$(cat "$HERE/LEVEL" 2>/dev/null || echo 1a.1)}"
if [[ -z "$JOBS" ]]; then
    JOBS="$(( $(nproc) / 2 ))"
fi
(( JOBS < 1 )) && JOBS=1

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

# Per-run scratch dir; each case runs in its own subdir so -j never races on
# the image copy, the BOOT patch, the transcript, or the monitor socket.
RES="$(mktemp -d /tmp/osb-golden-XXXXXX)"
trap 'rm -rf "$RES"' EXIT

# Run one case in a private temp dir; write "KIND|msg" to $RES/<i>. The
# parent prints the results in case order after `wait`, so -j output is
# deterministic (only the wall clock changes).
run_case() {
    local i="$1" name="$2" case="$3"
    local d tmpimg raw vga scrip bootprog="" ok=1 dt
    d="$(mktemp -d "$RES/run-$i-XXXXXX")"
    tmpimg="$d/img"; raw="$d/out"; vga="$d/vga"; scrip="$case"
    local t0=$SECONDS
    if ! cp "$IMG" "$tmpimg"; then
        echo "FAIL|image copy" > "$RES/$i"
        rm -rf "$d"
        return
    fi
    if grep -q '^boot ' "$case"; then
        bootprog="$(sed -n 's/^boot //p' "$case" | head -1)"
        scrip="$d/case.scm"
        grep -v '^boot ' "$case" > "$scrip"
    fi
    if [[ -n "$bootprog" ]] && ! patch_boot "$tmpimg" "$bootprog"; then
        echo "FAIL|BOOT patch" > "$RES/$i"
        rm -rf "$d"
        return
    fi
    if ! python3 "$HERE/drive.py" "$ISO" "$tmpimg" "$scrip" --out "$raw" --vga-out "$vga"; then
        echo "FAIL|driver error" > "$RES/$i"
        rm -rf "$d"
        return
    fi
    normalize "$name" < "$raw" > "$d/norm"
    if diff -u <(printf '%s\n' "$(cat "$HERE/cases/$name.out")") \
               <(printf '%s\n' "$(cat "$d/norm")") > "$HERE/cases/$name.diff"; then
        rm -f "$HERE/cases/$name.diff"
    else
        ok=0
    fi
    local vgaexp="$HERE/cases/$name.vga"
    if [[ -s "$vga" ]]; then
        if [[ ! -f "$vgaexp" ]]; then
            echo "FAIL|unexpected VGA dump; run recapture.sh" > "$RES/$i"
            rm -rf "$d"
            return
        fi
        if ! diff -u "$vgaexp" "$vga" > "$HERE/cases/$name.vga.diff"; then
            ok=0
        else
            rm -f "$HERE/cases/$name.vga.diff"
        fi
    elif [[ -f "$vgaexp" ]]; then
        echo "FAIL|missing VGA dump" > "$RES/$i"
        rm -rf "$d"
        return
    fi
    dt=$((SECONDS - t0))
    if (( ok )); then
        echo "PASS|$dt" > "$RES/$i"
    else
        echo "FAIL|diff in cases/$name.diff" > "$RES/$i"
    fi
    rm -rf "$d"
}

start=$SECONDS
shopt -s nullglob
names=(); paths=(); skips=()
i=0
for case in "$HERE"/cases/*.in "$HERE"/cases/*.scm; do
    name="$(basename "$case")"
    name="${name%.*}"
    min="$(case_level "$name")"
    min="${min%% *}"
    names[$i]="$name"
    paths[$i]="$case"
    skips[$i]=""
    if [[ -z "$min" ]]; then
        skips[$i]="no MANIFEST entry"
    elif (( $(level_idx "$min") > $(level_idx "$LEVEL") )); then
        skips[$i]="needs >= $min, at $LEVEL"
    fi
    i=$((i + 1))
done

for i in "${!names[@]}"; do
    if [[ -n "${skips[$i]}" ]]; then
        echo "SKIP|${skips[$i]}" > "$RES/$i"
        continue
    fi
    while (( $(jobs -rp | wc -l) >= JOBS )); do
        wait -n 2>/dev/null || true
    done
    run_case "$i" "${names[$i]}" "${paths[$i]}" &
done
wait

pass=0 fail=0 skip=0
for i in "${!names[@]}"; do
    name="${names[$i]}"
    r="$(cat "$RES/$i" 2>/dev/null || true)"
    [[ -n "$r" ]] || r="FAIL|no result"
    kind="${r%%|*}"
    msg="${r#*|}"
    case "$kind" in
        PASS)
            pass=$((pass + 1))
            if [[ -n "${OSB_TIMING:-}" ]]; then
                echo "PASS: $name (${msg}s)"
            else
                echo "PASS: $name"
            fi
            ;;
        SKIP)
            skip=$((skip + 1))
            echo "SKIP: $name ($msg)"
            ;;
        FAIL)
            fail=$((fail + 1))
            echo "FAIL: $name ($msg)"
            ;;
        *)
            fail=$((fail + 1))
            echo "FAIL: $name (bad result: $r)"
            ;;
    esac
done

echo "runtime: $((SECONDS - start))s | pass=$pass fail=$fail skip=$skip"
if [[ $fail -gt 0 ]]; then exit 1; fi
exit 0