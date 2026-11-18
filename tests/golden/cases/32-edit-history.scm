# 32-edit-history (spec, min-level 2): Up/Down recall of a wrapped line
# (A1/A2). Recalling a 100-char entry redraws it under the prompt across two
# rows; Down past the newest entry restores the (empty) draft.
nologin
expect login:
sendlf root
prompt
# A 100-char unknown command: echoed, wraps, runs silently (no output).
sendraw 636363636363636363636363636363636363636363636363636363636363636363636363636363636363636363636363636363636363636363636363636363636363636363636363636363636363636363636363636363636363636363636363636363630d
vga afterrun
# Up recalls the newest entry; the redraw must place the caret at the end.
sendraw 1b5b41
vga recall
# Down restores the empty draft.
sendraw 1b5b42
vga draft
ctrl-c
prompt
