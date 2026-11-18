# 34-escape (spec, min-level 2): ESC handling (A7). A lone ESC is discarded
# after a short wait (NextByte waits 5 ticks), so the next printable character
# is not eaten; a real CSI sequence still arrives atomically and is parsed.
nologin
expect login:
sendlf root
prompt
# Lone ESC, then (after the 5-tick timeout) 'x': x must be echoed.
sendraw 1b
sleep 0.4
sendraw 78
sendraw 03
prompt
# "AB" then Left x2: ESC [ D must still move the caret.
sendraw 4142
sendraw 1b5b44
sendraw 1b5b44
vga left2
sendraw 03
prompt
