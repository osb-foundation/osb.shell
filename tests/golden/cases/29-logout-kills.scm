# 29-logout-kills (spec, min-level 1c, I5): LOGOUT tears down the whole session.
# TLogout.EXE is the session leader: it logs in as "tester", spawns a background
# child (TSpin.EXE, loops forever), then calls LOGOUT. The child must be killed
# (WAIT -> -1) while the leader survives to print the result and the kernel
# session-0 tasks are untouched. Prints "LOGOUT: PASS". No login: the boot
# program IS the test.
nologin
boot A:\OSB\BIN\TLogout.EXE
expect LOGOUT: PASS