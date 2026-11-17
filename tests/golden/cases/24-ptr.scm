# 24-ptr (spec, min-level 1b): pointer hardening (S3). TPtr.EXE passes bad
# pointers (below UserBase, above UserLimit, crossing the limit, null) to
# every pointer-taking syscall (01h, 09h, 10h, 20h-23h, 6Ah) and checks each
# returns the expected error. Prints "TPTR: PASS" only if all pass and the
# kernel stays alive. No login: the boot program IS the test.
nologin
boot A:\OSB\BIN\TPtr.EXE
expect TPTR: PASS