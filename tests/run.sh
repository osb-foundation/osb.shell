#!/usr/bin/env bash
# Test tiers for the userspace shell (Phase 3, B8).
#
# Usage: tests/run.sh [smoke|unit|level|full]     (default: level)
#
#   smoke  boot the ISO headless, require the login prompt (~2 s)
#   unit   host-side unit tests for the line editor / completion (<5 s)
#   level  golden transcripts at tests/golden/LEVEL (-j GOLDEN_JOBS, default nproc/2)
#   full   level + mutation suite (11 patches, each caught)
#
# Env: OSB_ISO (default ../osb.kernel/osb.iso), GOLDEN_JOBS, OSB_MEM.
# The ISO is rebuilt (WITH_TESTS=1 for level/full, default for smoke) when the
# tier's image is not the one on disk.
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
ISO="${OSB_ISO:-$ROOT/../osb.kernel/osb.iso}"
IMG="$(dirname "$ISO")/osb.img"
TIER="${1:-level}"

# Gate: ABI version must agree across ABI.md, osbkt, and kernel (A7).
"$ROOT/../osbkt/tools/check-abi.sh" >/dev/null || { echo "ABI version mismatch; fix before testing" >&2; exit 1; }

usage() {
    echo "usage: tests/run.sh [smoke|unit|level|full]" >&2
    exit 2
}

# The WITH_TESTS image seeds tester/sudoer (level/full); the default image has
# root only and is what smoke's H-B check needs. Rebuild when the tier's image
# is not the one on disk.
iso_has_tests() {
    [[ -f "$IMG" ]] || return 1
    debugfs -R 'cat /OSB/CONF/PASSWD' "$IMG" 2>/dev/null | grep -q '^tester:'
}

ensure_iso() {
    local want="$1" have=0
    iso_has_tests && have=1
    if [[ ! -f "$ISO" || "$have" != "$want" ]]; then
        echo "building kernel image (WITH_TESTS=$want) ..."
        if [[ "$want" == "1" ]]; then
            WITH_TESTS=1 "$ROOT/../osb.kernel/build.sh" >/dev/null || return 1
        else
            "$ROOT/../osb.kernel/build.sh" >/dev/null || return 1
        fi
    fi
    [[ -f "$ISO" ]] || { echo "ERROR: ISO not found: $ISO" >&2; return 1; }
    return 0
}

case "$TIER" in
    smoke)
        ensure_iso 0 || exit 1
        exec "$HERE/smoke.sh" "$ISO"
        ;;
    unit)
        if [[ -d "$HERE/unit" ]]; then
            dotnet build -c Release "$HERE/unit" -v q --nologo || exit 1
            exec dotnet "$HERE/unit/bin/Release/net10.0/OsbShellUnit.dll"
        fi
        echo "SKIP: unit (tests/unit not present yet)" >&2
        exit 0
        ;;
    level)
        ensure_iso 1 || exit 1
        jobs="${GOLDEN_JOBS:-$(( $(nproc) / 2 ))}"
        (( jobs < 1 )) && jobs=1
        exec "$HERE/golden/golden.sh" "$ISO" -j "$jobs"
        ;;
    full)
        ensure_iso 1 || exit 1
        jobs="${GOLDEN_JOBS:-$(( $(nproc) / 2 ))}"
        (( jobs < 1 )) && jobs=1
        "$HERE/golden/golden.sh" "$ISO" -j "$jobs" || exit 1
        exec "$HERE/mutations.sh"
        ;;
    *)
        usage
        ;;
esac