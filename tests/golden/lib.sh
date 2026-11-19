# Shared helpers for the golden-transcript harness (A11 + F4).
#
# Golden transcripts are captured from the legacy in-kernel shell (osb.kernel
# commit 5078f4b, the last one where Kernel.cs called Shell.Run()) and are the
# parity fixtures for the userspace shell slices: each slice's acceptance run
# drives the current ISO over serial (drive.py) and diffs the normalized
# output. tests/golden/LEVEL tracks the level reached; MANIFEST maps each
# case to the level that must pass it (F4a).

# Ordered implementation levels. A slice updates tests/golden/LEVEL as it
# lands; cases whose min-level is above LEVEL are skipped.
LEVELS=(1a 1a.1 1b 1c 2 3 4 5 6 7 8 9 10 11)

level_idx() {
    local i=0 l
    for l in "${LEVELS[@]}"; do
        if [[ "$l" == "$1" ]]; then echo "$i"; return; fi
        i=$((i + 1))
    done
    echo -1
}

# case_level <case-name>: prints "<min-level> <kind>" from MANIFEST.
case_level() {
    awk -v c="$1" '$1 == c { print $2, $3 }' "$HERE/MANIFEST"
}

# normalize: strips the kernel boot banner, removes CR, masks volatile values
# (pid numbers, tick counters) so re-runs compare equal. Legacy/shell cases
# have a "login:" prompt: keep from the first one (that also drops any shell
# banner printed between the kernel banner and the prompt). nologin cases (the
# boot program IS the test, e.g. TWait.EXE) have no login prompt, so strip only
# the kernel banner block (everything through the first blank line) instead.
# Volatile masks are per-case (H3): only 16-jobs has pid/tick/round values.
normalize() {
    local case_name="${1:-}" tmp
    tmp="$(mktemp)"
    # Strip all CRs: the console emits CRLF and some ring-3 programs emit
    # "\r\n" on top of the console's own CR, so a line can end in "\r\r\n".
    sed -e 's/\r//g' > "$tmp"
    if grep -q 'login:' "$tmp"; then
        sed -n '/login:/,$p' "$tmp"
    else
        sed '1,/^$/d' "$tmp"
    fi |
        case "$case_name" in
            05-dir|14-fileops)
                # DIR timestamps are wall-clock ("MM/DD/YYYY HH:MM") and new
                # dirs show the epoch ("01/01/1970 00:00").
                sed -e 's#[0-9][0-9]/[0-9][0-9]/[0-9][0-9][0-9][0-9] [0-9][0-9]:[0-9][0-9]#<DATE>#g'
                ;;
            16-jobs)
                # DEMO's pid/tick counters and the WAIT pid are volatile.
                sed -e 's/pid [0-9][0-9]*/pid <N>/g' \
                    -e 's/tick [0-9][0-9]*/tick <T>/g' \
                    -e '/^DEMO \[[0-9][0-9]*\]: round/d'
                ;;
            99-stress)
                # Tick counters increase monotonically; DATE wall-clock and
                # TIME uptime differ between runs; mask for stability.
                sed -e 's#[0-9][0-9]/[0-9][0-9]/[0-9][0-9][0-9][0-9] [0-9][0-9]:[0-9][0-9]:[0-9][0-9]#<DATE>#g' \
                    -e 's/Uptime: [0-9][0-9]* s ([0-9][0-9]* ticks)/Uptime: <N> s (<T> ticks)/g' \
                    -e 's/Ticks: [0-9][0-9]*/Ticks: <T>/g'
                ;;
            *)
                cat
                ;;
        esac
    rm -f "$tmp"
}