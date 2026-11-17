# 25-getchar (spec, min-level 1b): GETCHAR (6Eh) line reader over serial AND
# PS/2. Boots SHELL.EXE (default boot program) and drives it:
#   * typing + Enter (serial CRLF)
#   * backspace erases a char
#   * Ctrl+C aborts a line (serial 0x03)
#   * typing + Enter over PS/2 (sendkey)
#   * Ctrl+C aborts a line (PS/2 Ctrl+C)
#   * LF-only, then CRLF (CRLF must not produce an extra empty line: the LF
#     right after a CR is swallowed), then CR-only LAST.
# Order matters: a CR sets the "swallow next LF" flag, so LF-only/CRLF run
# while it is clear and the lone CR is the final input.
# 1c: the shell now requires login, so the script logs in as root first (LF
# only, so no stray CR is left for the line reader) before the line-editor
# checks. The login itself exercises the same GETCHAR path.
nologin
expect login:
sendlf root
prompt
send ABC
prompt
sendraw 41425808430d
prompt
sendraw 68656c6c6f
ctrl-c
prompt
keynowait a
key ret
keynowait a
key ctrl-c
sendraw 0a
prompt
sendraw 58590d0a
prompt
sendraw 0d
prompt