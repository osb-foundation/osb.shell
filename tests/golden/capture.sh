#!/usr/bin/env bash
# Capture golden transcripts (A11 + F4c): drives a legacy-kernel ISO over
# serial with each case's directives and writes cases/<name>.out.
#
# Usage: tests/golden/capture.sh <iso> [img]
#   <iso>  kernel ISO whose in-kernel shell is the behavioral reference
#          (osb.kernel @ 5078f4b, or any build that still calls Shell.Run())
#   [img]  matching ext4 image (default: <dirname iso>/osb.img)
#
# Writes cases/<name>.out next to the case file. Env: BOOT_DELAY, OSB_MEM.
#
# Rebuilding the legacy reference (osb.kernel @ 5078f4b) needed two shims in
# this environment, applied to a throwaway checkout only:
#   - a clang wrapper -> gcc (no clang installed): printf + chmod as in the
#     project history, or `CC=gcc` on a build.sh that honors $CC;
#   - the XWIN_ROOT guard the current build.sh carries (the 5078f4b script
#     aborts under `set -e` when ../Osb.Xwin is absent).
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=lib.sh
source "$HERE/lib.sh"

ISO="${1:?usage: capture.sh <iso> [img]}"
IMG="${2:-$(dirname "$ISO")/osb.img}"

[[ -f "$ISO" ]] || { echo "ERROR: ISO not found: $ISO" >&2; exit 1; }
[[ -f "$IMG" ]] || { echo "ERROR: disk image not found: $IMG" >&2; exit 1; }

start=$SECONDS
shopt -s nullglob
for case in "$HERE"/cases/*.in "$HERE"/cases/*.scm; do
    name="$(basename "$case")"
    name="${name%.*}"
    echo "capturing $name ..."
    # Each case runs against its own throwaway copy of the disk image:
    # mutating cases (USER /ADD, MD, DEL) must not leak into other cases.
    tmp="$(mktemp /tmp/osb-cap-XXXXXX.raw)"
    tmpimg="$(mktemp /tmp/osb-img-XXXXXX.img)"
    cp "$IMG" "$tmpimg"
    if python3 "$HERE/drive.py" "$ISO" "$tmpimg" "$case" --out "$tmp"; then
        normalize "$name" < "$tmp" > "$HERE/cases/$name.out"
    else
        echo "WARN: $name driver failed; writing partial transcript" >&2
        normalize "$name" < "$tmp" > "$HERE/cases/$name.out"
    fi
    rm -f "$tmp" "$tmpimg"
done
echo "runtime: $((SECONDS - start))s | done."