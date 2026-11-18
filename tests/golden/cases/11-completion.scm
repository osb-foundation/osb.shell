# 11-completion (spec, min-level 2): TAB completion of the token at the caret
# (S2.3). Driven over serial with raw bytes so TAB (0x09) reaches the line
# editor exactly as a PS/2 Tab would (ConsoleInput cooks both to 0x09).
#   * unique command prefix: "HI" + TAB -> "HISTORY", then Enter runs it and
#     lists the seeded history (proves the completion landed in the buffer)
#   * no match: "ZZZ" + TAB -> line unchanged, unknown command, no output
#   * file completion: "CD T" + TAB -> "CD TRASH\" (a directory gains a '\')
nologin
expect login:
sendlf root
prompt
send ECHO one
prompt
send ECHO two
prompt
sendraw 4849090d
prompt
sendraw 5a5a5a090d
prompt
sendraw 43442054090d
prompt