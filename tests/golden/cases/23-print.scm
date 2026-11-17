# 23-print (spec, min-level 1b): WRITE (01h) text output. TPrint.EXE checks
# that a null/empty PrintString is a no-op (no fault), that '$' is literal
# (not a terminator), and that a 300-char line survives (not truncated at
# 255). No login: the boot program IS the test.
nologin
boot A:\OSB\BIN\TPrint.EXE
expect len300:
expect end