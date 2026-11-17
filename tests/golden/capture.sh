#!/usr/bin/env bash
# Capture golden transcripts from a legacy-kernel ISO (A11).
#
# Usage: tests/golden/capture.sh <iso> [img]
#   <iso>  kernel ISO whose in-kernel shell is the behavioral reference
#          (osb.kernel @ 5078f4b, or any build that still calls Shell.Run())
#   [img]  matching ext4 image (default: <dirname iso>/osb.img)
#
# Writes cases/<name>.out next to cases/<name>.in. Env: BOOT_DELAY, SETTLE,
# OSB_MEM (see lib.sh).
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

shopt -s nullglob
for inf in "$HERE"/cases/*.in; do
    name="$(basename "$inf" .in)"
    echo "capturing $name ..."
    run_case "$ISO" "$IMG" "$inf" | normalize > "$HERE/cases/$name.out"
done
echo "done."