# 24-ptr (spec, min-level 1b): pointer hardening (S3) + B5 expanded list.
# TPtr.EXE passes bad pointers (below UserBase, above UserLimit, crossing the
# limit, true u64 wrap, null) to every pointer-taking syscall (01h, 09h, 10h,
# 20h-23h, 6Ah, 42h, 48h, 50h, 56h) and checks each returns the expected error.
# 20h also tests rsi=100000 (out-of-range length) and the byte-rule path
# crossing. Prints "TPTR: PASS n=<count>" only if all pass; the count matches
# the cases it ran (35). No login: the boot program IS the test.
nologin
boot A:\OSB\BIN\TPtr.EXE
expect TPTR: PASS n=35