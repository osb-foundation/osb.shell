# 29-logout-kills (spec, min-level 1c, I5): LOGOUT tears down the whole session.
# TLogout.EXE is the session leader: it logs in as "tester", spawns a background
# child (TSpin.EXE, loops forever), then calls LOGOUT. Checks:
#   Q2 the child is killed (WAIT -> -1); after LOGOUT every privileged call is
#      refused (WHOAMI/SUDO/POWEROFF/REBOOT/spawn ROOTONLY == -3); a fresh LOGIN
#      works.
#   Q5 a re-LOGIN as another user ("sudoer") tears the session down before
#      authenticating (the freshly spawned child is gone).
# The kernel session-0 tasks are untouched. Prints "LOGOUT: PASS". No login: the
# boot program IS the test.
nologin
boot A:\OSB\BIN\TLogout.EXE
expect LOGOUT: PASS