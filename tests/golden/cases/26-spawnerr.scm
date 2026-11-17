# 26-spawnerr (spec, min-level 1b): SPAWNPATH (6Ah) error contract (B3/B4).
# TSpawnErr.EXE checks: ENOENT (-4), EACCES (-3, TNX.EXE mode 0644 -> no child
# created), EINVAL (-5, argsLen 129 and pathLen 0), EFAULT (-1, bad path/args
# pointers), and args delivered intact at 128 bytes (TArgs prints all 128).
# Prints "TSPAWNERR: PASS n=<count>" (7). No login: the boot program IS the test.
nologin
boot A:\OSB\BIN\TSpawnErr.EXE
expect TSPAWNERR: PASS n=7
expect args=[AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA]