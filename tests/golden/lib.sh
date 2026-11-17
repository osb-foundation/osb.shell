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

# normalize: strips the kernel boot banner (everything before the first
# login prompt or first prompt line), removes CR, masks volatile values
# (DIR dates, pid numbers) so re-runs compare equal. The tick/DEMO masks are
# per-case (H3): only cases that run DEMO get them, and the volatile `:Nms`
# ping-time mask is gone (no case pings).
normalize() {
    local case_name="${1:-}"
    sed -n '/login:/,$p' |
        sed -e 's/\r$//' \
            -e 's#[0-9][0-9]/[0-9][0-9]/[0-9][0-9][0-9][0-9] [0-9][0-9]:[0-9][0-9]#<DATE>#g' \
            -e 's/pid [0-9][0-9]*/pid <N>/g' |
        case "$case_name" in
            16-jobs)
                # DEMO's round/tick counters are wall-clock dependent: mask
                # the tick and drop the round lines entirely.
                sed -e 's/tick [0-9][0-9]*/tick <T>/g' \
                    -e '/^DEMO \[[0-9][0-9]*\]: round/d'
                ;;
            *)
                cat
                ;;
        esac
}