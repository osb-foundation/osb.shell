# 28-identity (spec, min-level 1c, I5): kernel-owned identity/authorization.
# TId.EXE logs in as the non-root "tester" and checks every failure path:
#   LOGIN ok (0); bad password (-3); unknown user (-3, indistinguishable);
#   SUDO as non-sudoer (-3); POWEROFF (-3); REBOOT (-3); spawn of a root-owned
#   0700 file (-3, no task); a child inherits the non-root uid and cannot
#   LOGIN/LOGOUT (Q1); an empty name (-3) and an over-long name (>= NameCap,
#   Q3) are rejected, not truncated.
# Prints "TID: PASS n=10". No login: the boot program IS the test.
nologin
boot A:\OSB\BIN\TId.EXE
expect TID: PASS n=10