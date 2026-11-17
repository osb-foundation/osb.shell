# 30-sudo-scope (spec, min-level 1c, I5): SUDO elevation scope.
# TSudo.EXE logs in as the non-root sudoer "sudoer", calls SUDO, and checks:
#   #1 a child spawned while elevated inherits uid 0 (it can spawn the root-owned
#      0700 ROOTONLY.EXE),
#   #2 END_SUDO restores the caller (it can no longer spawn ROOTONLY.EXE).
# Elevation is per-task (the child keeps its inherited uid even after the parent
# de-elevates; a task's elevation is dropped when its slot is freed). Prints
# "TSUDO: PASS". No login: the boot program IS the test.
nologin
boot A:\OSB\BIN\TSudo.EXE
expect TSUDO: PASS