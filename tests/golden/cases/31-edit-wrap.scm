# 31-edit-wrap (spec, min-level 2): editing a line that wraps past 80 columns
# (A1/A2). The old "\r"-based redraw returned to column 0 of the CURRENT row,
# so any edit on a wrapped line garbled the screen; the "\b"-based redraw must
# reposition with the wrap-aware backspace and leave the hardware cursor on the
# caret. Every `vga` block is the 80x25 text buffer + the CRTC cursor.
nologin
expect login:
sendlf root
prompt
# 100 chars at the prompt (21 cols): the line occupies two rows.
sendraw 61616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161
vga typed
# Left x30: the caret moves back one row.
sendraw 1b5b441b5b441b5b441b5b441b5b441b5b441b5b441b5b441b5b441b5b441b5b441b5b441b5b441b5b441b5b441b5b441b5b441b5b441b5b441b5b441b5b441b5b441b5b441b5b441b5b441b5b441b5b441b5b441b5b441b5b44
vga left30
# Insert two chars mid-line: full redraw, caret after the insert.
sendraw 5a5a
vga insert
# Home: caret to the start of the line (wrap-aware backspaces).
sendraw 1b5b48
vga home
# Right x5.
sendraw 1b5b431b5b431b5b431b5b431b5b43
vga right5
# Delete at the caret.
sendraw 1b5b337e
vga delete
# Backspace before the caret.
sendraw 7f
vga backspace
ctrl-c
prompt
