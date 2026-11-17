# Shared helpers for the golden-transcript harness (A11).
#
# Golden transcripts are captured from the legacy in-kernel shell (osb.kernel
# commit 5078f4b, the last one where Kernel.cs called Shell.Run()) and are the
# parity fixtures for the userspace shell slices: each slice's acceptance run
# feeds the same commands to the current ISO and diffs the normalized output.

# Boot delay: seconds from QEMU start until the login prompt is expected.
BOOT_DELAY="${BOOT_DELAY:-12}"
# Settle time: seconds to keep feeding/guest running after the input is sent.
SETTLE="${SETTLE:-10}"

# run_case ISO IMG INFILE
# Boots ISO headless, feeds INFILE over serial (LF -> CRLF on the wire),
# prints the raw serial transcript to stdout. QEMU is killed by timeout (rc
# 124 is expected and ignored).
run_case() {
    local iso="$1" img="$2" inf="$3"
    (sleep "$BOOT_DELAY"; sed 's/$/\r/' "$inf"; sleep "$SETTLE") |
        timeout $((BOOT_DELAY + SETTLE + 15)) qemu-system-x86_64 \
            -cdrom "$iso" -m "${OSB_MEM:-64M}" \
            -drive file="$img",if=ide,index=0,format=raw \
            -vga std -display none -monitor none -no-reboot -serial stdio \
            2>/dev/null || true
}

# normalize
# Strips the kernel boot banner (everything before the first login prompt),
# removes CR, and masks volatile dates (DIR timestamps change per image build).
normalize() {
    sed -n '/login:/,$p' |
        sed -e 's/\r$//' \
            -e 's#[0-9][0-9]/[0-9][0-9]/[0-9][0-9][0-9][0-9] [0-9][0-9]:[0-9][0-9]#<DATE>#g'
}