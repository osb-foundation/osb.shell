#!/usr/bin/env bash
# Recapture the SPEC golden transcripts of the CURRENT shell/kernel image.
#
# Usage: tests/golden/recapture.sh <iso> <case> [<case> ...]
#   <iso>   kernel ISO built from the current tree (../osb.kernel/osb.iso)
#   <case>  case name without extension (e.g. 11-completion, 31-edit-wrap)
#
# Legacy-parity cases (kind=legacy in MANIFEST) must NOT be recaptured here:
# their golden is captured from the in-kernel shell with capture.sh. This
# script is for kind=spec cases (11-completion, 31/32/33, ...) whose expected
# output is the new shell's behavior.
#
# For each case it writes cases/<case>.out (normalized serial transcript) and,
# when the script uses `vga`/`vgacursor` directives, cases/<case>.vga. An
# empty VGA capture removes a stale .vga golden.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=lib.sh
source "$HERE/lib.sh"

ISO="${1:?usage: recapture.sh <iso> <case> [...]}"
shift
[[ $# -ge 1 ]] || { echo "usage: recapture.sh <iso> <case> [...]" >&2; exit 2; }
[[ -f "$ISO" ]] || { echo "ERROR: ISO not found: $ISO" >&2; exit 1; }
IMG="$(dirname "$ISO")/osb.img"
[[ -f "$IMG" ]] || { echo "ERROR: disk image not found: $IMG" >&2; exit 1; }

start=$SECONDS
for name in "$@"; do
    case_file=""
    for ext in scm in; do
        [[ -f "$HERE/cases/$name.$ext" ]] && case_file="$HERE/cases/$name.$ext" && break
    done
    [[ -n "$case_file" ]] || { echo "ERROR: no cases/$name.scm|in" >&2; exit 1; }
    kind="$(case_level "$name")"; kind="${kind#* }"
    if [[ "$kind" == "legacy" ]]; then
        echo "WARN: $name is kind=legacy - use capture.sh (legacy ISO), not recapture.sh" >&2
    fi
    echo "recapturing $name ..."
    tmp="$(mktemp /tmp/osb-cap-XXXXXX.raw)"
    tmpimg="$(mktemp /tmp/osb-img-XXXXXX.img)"
    tmpvga="$(mktemp /tmp/osb-vga-XXXXXX.txt)"
    cp "$IMG" "$tmpimg"
    if python3 "$HERE/drive.py" "$ISO" "$tmpimg" "$case_file" \
            --out "$tmp" --vga-out "$tmpvga"; then
        normalize "$name" < "$tmp" > "$HERE/cases/$name.out"
        if [[ -s "$tmpvga" ]]; then
            cp "$tmpvga" "$HERE/cases/$name.vga"
        else
            rm -f "$HERE/cases/$name.vga"
        fi
    else
        echo "WARN: $name driver failed; writing partial transcript" >&2
        normalize "$name" < "$tmp" > "$HERE/cases/$name.out"
    fi
    rm -f "$tmp" "$tmpimg" "$tmpvga"
done
echo "runtime: $((SECONDS - start))s | done."