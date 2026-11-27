# M7.1: OSLANG.EXE runtime error boundary proof (min-level 1a.1)
# Boots TOSLANGError.EXE which spawns OSLANG.EXE with DIVZERO.OSL,
# waits for it, and verifies:
# 1. Output contains "Runtime error: Division by zero."
# 2. Exit code is non-zero (expected: 1)
nologin
boot A:\OSB\BIN\TOSLANGError.EXE
expect TOSLANGERROR: child exit=1 PASS