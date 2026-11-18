# 33-complete-wrap (spec, min-level 2): TAB completion on a wrapped line
# (A1/A2). A case-mismatched single match redraws the whole line with the
# canonical name (lowercase and mixed-case prefixes); an ambiguous match
# lists and reprints the prompt + line.
nologin
expect login:
sendlf root
prompt
# The line wraps; the last token is a lowercase path -> full redraw.
sendraw 4543484f2078787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787820413a5c4f53425c524f4f545c747209
vga casefix
ctrl-c
prompt
# Ambiguous directory listing on a wrapped line: names, then prompt + line.
sendraw 4543484f2078787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787820413a5c4f53425c09
vga listing
ctrl-c
prompt
# Mixed-case path token -> canonical redraw.
sendraw 4543484f2078787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787878787820413a5c4f53425c524f4f545c745209
vga mixedcase
ctrl-c
prompt
