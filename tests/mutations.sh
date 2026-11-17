#!/usr/bin/env bash
# Mutation testing for the 1c identity/session checks (Q7).
#
# Each tests/mutations/NN-name.patch breaks exactly one kernel security check.
# For every patch this script:
#   1. applies it to the osb.kernel tree,
#   2. rebuilds the ISO (WITH_TESTS=1),
#   3. boots the case that exercises the broken check and asserts it FAILS,
#   4. reverts the patch.
# A patch is "caught" (good) when the case does NOT pass; a patch that still
# passes is a hole in the tests (bad). Builds are ~30s, runs ~20s each.
#
# Usage: tests/mutations.sh [NN-name ...]   (default: all patches)
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"          # osb.shell/tests
SHELL="$(cd "$HERE/.." && pwd)"                # osb.shell
GOLDEN="$HERE/golden"
KROOT="$(cd "$SHELL/../osb.kernel" && pwd)"
ISO="$KROOT/osb.iso"
IMG="$KROOT/osb.img"
MUT="$HERE/mutations"
[[ -f "$ISO" ]] || { echo "ERROR: $ISO missing (run ../osb.kernel/build.sh first)" >&2; exit 2; }
[[ -f "$IMG" ]] || { echo "ERROR: $IMG missing" >&2; exit 2; }

# patch -> case that must catch it.
case_for() {
    case "$1" in
        M06-*) echo 30-sudo-scope ;;
        M10-*) echo 29-logout-kills ;;
        *)     echo 28-identity ;;
    esac
}

# Run one golden case against the current ISO/IMG; return 0 if it PASSES.
run_case() {
    local name="$1" casefile rc
    casefile="$GOLDEN/cases/$name.scm"
    [[ -f "$casefile" ]] || casefile="$GOLDEN/cases/$name.in"
    local tmpimg scrip out bf
    tmpimg="$(mktemp /tmp/osb-mut-img-XXXXXX.img)"; cp "$IMG" "$tmpimg"
    if grep -q '^boot ' "$casefile"; then
        bf="$(mktemp)"; sed -n 's/^boot //p' "$casefile" | head -1 > "$bf"
        debugfs -w -R "rm /OSB/CONF/BOOT" "$tmpimg" >/dev/null 2>&1 || true
        debugfs -w -R "write $bf /OSB/CONF/BOOT" "$tmpimg" >/dev/null 2>&1
        e2fsck -fy "$tmpimg" >/dev/null 2>&1 || true
        rm -f "$bf"
    fi
    scrip="$(mktemp /tmp/osb-mut-scm-XXXXXX.scm)"; grep -v '^boot ' "$casefile" > "$scrip"
    out="$(mktemp /tmp/osb-mut-out-XXXXXX.raw)"
    python3 "$GOLDEN/drive.py" "$ISO" "$tmpimg" "$scrip" --out "$out" >/dev/null 2>&1
    rc=$?
    rm -f "$tmpimg" "$scrip"
    # PASS iff drive.py succeeded (rc 0). On failure, surface the FAIL line.
    if [[ $rc -eq 0 ]]; then
        echo PASS
    else
        local reason
        reason="$(grep -aE 'FAIL|TID: FAIL|LOGOUT: FAIL|TSUDO: FAIL' "$out" | tail -1 | tr -d '\r')"
        [[ -n "$reason" ]] || reason="no pass line (rc=$rc)"
        rm -f "$out"
        echo "$reason"
    fi
    [[ $rc -eq 0 ]] && rm -f "$out"
}

names=("$@")
if [[ ${#names[@]} -eq 0 ]]; then
    shopt -s nullglob
    names=()
    for p in "$MUT"/*.patch; do names+=("$(basename "$p" .patch)"); done
fi

printf '%-24s %-18s %-10s %s\n' PATCH CASE RESULT DETAIL
printf '%-24s %-18s %-10s %s\n' ----- ---- ------ ------
caught=0; missed=0; broken=0
for n in "${names[@]}"; do
    patch="$MUT/$n.patch"
    [[ -f "$patch" ]] || { printf '%-24s %-18s %-10s %s\n' "$n" - MISSING "no such patch"; broken=$((broken+1)); continue; }
    cse="$(case_for "$n")"
    if ! patch -p1 -d "$KROOT" < "$patch" >/dev/null 2>&1; then
        printf '%-24s %-18s %-10s %s\n' "$n" "$cse" APPLY-FAIL "patch did not apply"
        broken=$((broken+1)); continue
    fi
    if ! ( cd "$KROOT" && WITH_TESTS=1 ./build.sh ) >/tmp/mut-build.log 2>&1; then
        printf '%-24s %-18s %-10s %s\n' "$n" "$cse" BUILD-FAIL "$(grep -m1 'error CS' /tmp/mut-build.log || echo 'see /tmp/mut-build.log')"
        patch -R -p1 -d "$KROOT" < "$patch" >/dev/null 2>&1
        broken=$((broken+1)); continue
    fi
    res="$(run_case "$cse")"
    patch -R -p1 -d "$KROOT" < "$patch" >/dev/null 2>&1
    if [[ "$res" == PASS ]]; then
        printf '%-24s %-18s %-10s %s\n' "$n" "$cse" MISSED "case still passed"
        missed=$((missed+1))
    else
        printf '%-24s %-18s %-10s %s\n' "$n" "$cse" caught "$res"
        caught=$((caught+1))
    fi
done
printf '\ncaught=%d missed=%d broken=%d\n' "$caught" "$missed" "$broken"
[[ $missed -eq 0 && $broken -eq 0 ]]