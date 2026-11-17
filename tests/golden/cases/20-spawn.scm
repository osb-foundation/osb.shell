# 20-spawn (spec, min-level 1b): SPAWN a built-in program (HELLO) and WAIT
# for it. TSpawn.EXE SPAWN(0) + WAIT, prints the child exit code, and must
# print "TSPAWN: child exit=0 PASS" (HELLO exits 0). The HELLO banner appears
# before the TSPAWN line. No login: the boot program IS the test.
nologin
boot A:\OSB\BIN\TSpawn.EXE
expect TSPAWN: child exit=0 PASS