# 27-ctrlc-order (spec, min-level 1c, P0.2): Ctrl+C is IN BAND.
# One serial burst "abc" + 0x03 (Ctrl+C) + "def" + CR must abort the "abc"
# line (it is echoed as typed but NOT executed) and then run "def" as a new
# line, in that order. Before the in-band fix the Ctrl+C was counted out of
# band and bytes were drained first, so the shell saw "abcdef" and echoed it.
# 1c: the shell requires login, so log in as root first (LF only) before the
# in-band Ctrl+C burst.
nologin
expect login:
sendlf root
prompt
sendraw 616263036465660d
prompt