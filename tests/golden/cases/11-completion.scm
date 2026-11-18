# 11-completion (spec, min-level 2): TAB completion of the token at the caret
# (S2.3). Driven over serial with raw bytes so TAB (0x09) reaches the line
# editor exactly as a PS/2 Tab would (ConsoleInput cooks both to 0x09).
#   * unique command prefix: "HI" + TAB -> "HISTORY", then Enter runs it and
#     lists the seeded history (proves the completion landed in the buffer)
#   * no match: "ZZZ" + TAB -> line unchanged, unknown command, no output
#   * file completion: "CD T" + TAB -> "CD TRASH\" (a directory gains a '\')
#   * empty token: TAB on an empty line does nothing
#   * ambiguous command prefix: "H" + TAB lists HELP/HALT/HISTORY and redraws
#   * absolute path: "CD A:\OSB\B" + TAB -> "CD A:\OSB\BIN\"
#   * root-anchored path: "CD \OSB\B" + TAB -> "CD \OSB\BIN\"
#   * subdir path: "CD A:\OSB\ROOT\T" + TAB -> "...\TRASH\"
#   * no match in a directory: "CD A:\OSB\Z" + TAB -> unchanged
#   * token longer than the completion name cap (32): nothing happens
#   * lowercase/mixed-case command prefixes canonicalize (ver/vEr -> VER)
#   * token at the line cap (127 chars, no spaces): nothing happens
# The path cases are discarded with Ctrl+C so the cwd stays A:\OSB\ROOT.
# A directory with more than MaxMatches (64) matching entries is not covered
# here: it needs a seeded directory (deferred, see MIGRATION.md slice 3).
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
# empty token: TAB on an empty line is a no-op
sendraw 090d
prompt
# ambiguous command prefix "H": HELP  HALT  HISTORY, line stays "H"
sendraw 48090d
prompt
# CD A:\OSB\B + TAB, discarded with Ctrl+C
sendraw 434420413a5c4f53425c420903
prompt
# CD \OSB\B + TAB, discarded with Ctrl+C
sendraw 4344205c4f53425c420903
prompt
# CD A:\OSB\ROOT\T + TAB, discarded with Ctrl+C
sendraw 434420413a5c4f53425c524f4f545c540903
prompt
# CD A:\OSB\Z + TAB, discarded with Ctrl+C
sendraw 434420413a5c4f53425c5a0903
prompt
# over-long token (40 chars > the 32-char completion cap)
sendraw 454545454545454545454545454545454545454545454545454545454545454545454545454545450903
prompt
# lowercase and mixed-case command prefixes canonicalize to VER
sendraw 766572090d
prompt
sendraw 764572090d
prompt
# token at the line cap (127 chars, no spaces): no completion
sendraw 45454545454545454545454545454545454545454545454545454545454545454545454545454545454545454545454545454545454545454545454545454545454545454545454545454545454545454545454545454545454545454545454545454545454545454545454545454545454545454545454545454545454545090d
prompt
