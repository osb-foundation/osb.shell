# 6Ah SPAWNPATH + WAIT proof (spec, min-level 1a.1): the kernel boots TWait.EXE
# via A:\OSB\CONF\BOOT (the `boot` directive below), which SPAWNPATHs
# TExit.EXE (exit code 7), waits, and must print exactly 7 to PASS. TExit's own
# "TEXIT: exiting with code 7" line precedes the TWAIT line in the transcript.
nologin
boot A:\OSB\BIN\TWait.EXE
expect TWAIT: child exit=7 PASS