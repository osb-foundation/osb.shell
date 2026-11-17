#!/usr/bin/env bash
# Smoke test (A11 harness): boot the kernel ISO headless, capture the serial
# console, and require the userspace shell banner. Exit 0 on success.
#
# Usage: tests/smoke.sh [path-to-osb.iso]     (default: ../osb.kernel/osb.iso)
# Env:   OSB_MEM (guest RAM, default 64M), OSB_TIMEOUT (seconds, default 45).
#
# Later slices extend this harness with golden transcripts
# (tests/golden/, captured from the legacy in-kernel shell): feed commands
# over serial and diff the output. See docs/MIGRATION.md A11.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ISO="${1:-$ROOT/../osb.kernel/osb.iso}"
IMG="$(dirname "$ISO")/osb.img"
MEM="${OSB_MEM:-64M}"
TMO="${OSB_TIMEOUT:-45}"

[[ -f "$ISO" ]] || { echo "ERROR: ISO not found: $ISO (run ../osb.kernel/build.sh first)" >&2; exit 1; }
[[ -f "$IMG" ]] || { echo "ERROR: disk image not found: $IMG (run ../osb.kernel/build.sh first)" >&2; exit 1; }

LOG="$(mktemp /tmp/osb-smoke-XXXXXX.log)"
trap 'rm -f "$LOG"' EXIT

echo "Booting $ISO (serial -> $LOG, ${TMO}s timeout) ..."
rc=0
timeout "$TMO" qemu-system-x86_64 \
    -cdrom "$ISO" -m "$MEM" \
    -drive file="$IMG",if=ide,index=0,format=raw \
    -netdev user,id=net0 -device e1000,netdev=net0 \
    -vga std -display none -monitor none -no-reboot \
    -serial "file:$LOG" || rc=$?
# 124 = timeout hit (expected: the 1a shell idles forever), anything else is
# a QEMU failure.
if [[ $rc -ne 0 && $rc -ne 124 ]]; then
    echo "ERROR: QEMU exited with $rc" >&2
    exit 1
fi

if grep -q "OSB Userspace Shell v0.72" "$LOG"; then
    echo "PASS: userspace shell banner found on serial."
else
    echo "FAIL: banner not found. Last serial lines:" >&2
    tail -20 "$LOG" >&2 || true
    exit 1
fi

# H-B: the default image must always boot SHELL.EXE regardless of CONF/BOOT.
# Patch CONF/BOOT on a throwaway copy to point at a non-shell program and
# verify the default kernel still boots the shell (CONF/BOOT is honored only
# in WITH_TESTS=1 builds).
if [[ -f "$IMG" ]] && command -v debugfs >/dev/null 2>&1; then
    TMPIMG="$(mktemp /tmp/osb-smoke-XXXXXX.img)"
    cp "$IMG" "$TMPIMG"
    BF="$(mktemp)"
    printf 'A:\\OSB\\BIN\\TExit.EXE\n' > "$BF"
    debugfs -w -R "rm /OSB/CONF/BOOT" "$TMPIMG" 2>/dev/null || true
    debugfs -w -R "write $BF /OSB/CONF/BOOT" "$TMPIMG" 2>/dev/null || true
    e2fsck -fy "$TMPIMG" >/dev/null 2>&1 || true
    rm -f "$BF"
    HBL="$(mktemp /tmp/osb-smoke-hb-XXXXXX.log)"
    trap 'rm -f "$LOG" "$HBL" "$TMPIMG"' EXIT
    hrc=0
    timeout "$TMO" qemu-system-x86_64 \
        -cdrom "$ISO" -m "$MEM" \
        -drive file="$TMPIMG",if=ide,index=0,format=raw \
        -netdev user,id=net0 -device e1000,netdev=net0 \
        -vga std -display none -monitor none -no-reboot \
        -serial "file:$HBL" || hrc=$?
    if [[ $hrc -ne 0 && $hrc -ne 124 ]]; then
        echo "ERROR: H-B QEMU exited with $hrc" >&2
        rm -f "$HBL" "$TMPIMG"
        exit 1
    fi
    if grep -q "OSB Userspace Shell v0.72" "$HBL"; then
        echo "PASS (H-B): default image boots SHELL.EXE despite CONF/BOOT."
    else
        echo "FAIL (H-B): default image honored CONF/BOOT (should boot SHELL.EXE)." >&2
        tail -20 "$HBL" >&2 || true
        rm -f "$HBL" "$TMPIMG"
        exit 1
    fi
    rm -f "$HBL" "$TMPIMG"
fi

exit 0