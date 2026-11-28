# Card 2: OSLANG variable round-trip test for freestanding NativeAOT
# Boots OSLANG.EXE (via CONF/BOOT patch) which runs HELLO.OSL (replaced with var-test.osl)
nologin
expect === String variable test ===
expect hello world
expect === Number variable test ===
expect 42
expect === Boolean variable test ===
expect TRUE
expect === String literal test (should work before and after fix) ===
expect literal string
expect === Number literal test ===
expect 123
expect === Boolean literal test ===
expect TRUE
expect === Multiple variables ===
expect 10
expect 20
expect 30
expect === Test complete ===
expect Boot program exited. Halting.
