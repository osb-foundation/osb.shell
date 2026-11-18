#!/usr/bin/env bash
# Legacy parity (Phase 3, B5): re-run the legacy-kernel reference cases and
# diff the normalized transcript against tests/golden/legacy/<case>.out.
#
# The legacy ISO is the in-kernel shell at osb.kernel @ 5078f4b (the last
# commit that called Shell.Run()); it is NOT in this repo. Without it the
# parity is skipped with a message (not silently passed).
#
# Skips when the inputs are unchanged: the hash of drive.py, lib.sh (the
# normalize rules) and legacy/*.out is stored in legacy/.parity.stamp. Use
# --force to re-run anyway.
#
# Usage: tests/golden/parity.sh [--force] [--repeat N] [legacy-iso] [legacy-img]
#   --repeat N  run the five cases N times (default 1); all must match.
# Exit: 0 = parity OK or skipped, 1 = a legacy transcript changed, 2 = usage.
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=lib.sh
source "$HERE/lib.sh"

ISO=""
IMG=""
FORCE=0
REPEAT=1
while [[ $# -gt 0 ]]; do
    case "$1" in
        --force) FORCE=1; shift ;;
        --repeat) REPEAT="${2:?usage: parity.sh [--force] [--repeat N] [iso] [img]}"; shift 2 ;;
        -*) echo "usage: parity.sh [--force] [--repeat N] [legacy-iso] [legacy-img]" >&2; exit 2 ;;
        *) if [[ -z "$ISO" ]]; then ISO="$1"; elif [[ -z "$IMG" ]]; then IMG="$1"; else
               echo "usage: parity.sh [--force] [--repeat N] [legacy-iso] [legacy-img]" >&2; exit 2
           fi
           shift ;;
    esac
done

CASES=(04-whoami 10-history 12-login 25-getchar 27-ctrlc-order)
HASH="$( { sha256sum "$HERE/drive.py" "$HERE/lib.sh" "$HERE"/legacy/*.out; } | sha256sum | cut -d' ' -f1)"
STAMP="$HERE/legacy/.parity.stamp"

if [[ $FORCE -eq 0 && -f "$STAMP" && "$(cat "$STAMP")" == "$HASH" ]]; then
    echo "SKIP: legacy parity unchanged ($HASH)"
    exit 0
fi
if [[ -z "$ISO" || ! -f "$ISO" ]]; then
    echo "SKIP: no legacy ISO (pass one to run parity; legacy/ has the reference transcripts)"
    exit 0
fi
IMG="${IMG:-$(dirname "$ISO")/osb.img}"
[[ -f "$IMG" ]] || { echo "ERROR: image not found: $IMG" >&2; exit 2; }

start=$SECONDS
fail=0
for rep in $(seq 1 "$REPEAT"); do
    for name in "${CASES[@]}"; do
        casefile="$HERE/cases/$name.scm"
        [[ -f "$casefile" ]] || casefile="$HERE/cases/$name.in"
        [[ -f "$casefile" ]] || { echo "MISSING: cases/$name.scm|in" >&2; fail=1; continue; }
        tmpimg="$(mktemp /tmp/osb-parity-img-XXXXXX.img)"
        out="$(mktemp /tmp/osb-parity-out-XXXXXX.raw)"
        cp "$IMG" "$tmpimg"
        if ! python3 "$HERE/drive.py" "$ISO" "$tmpimg" "$casefile" --out "$out" >/dev/null 2>&1; then
            echo "FAIL: $name (driver)"
            rm -f "$tmpimg" "$out"
            fail=1
            continue
        fi
        normalize "$name" < "$out" > "$out.n"
        if diff -u "$HERE/legacy/$name.out" "$out.n" > "$HERE/legacy/$name.diff"; then
            rm -f "$HERE/legacy/$name.diff"
            [[ $rep -eq 1 ]] && echo "PASS: $name"
        else
            echo "FAIL: $name (diff in legacy/$name.diff)"
            fail=1
        fi
        rm -f "$tmpimg" "$out" "$out.n"
    done
done

if [[ $fail -eq 0 ]]; then
    echo "$HASH" > "$STAMP"
    echo "parity: OK ($((SECONDS - start))s, repeat=$REPEAT)"
    exit 0
fi
echo "parity: FAILED"
exit 1