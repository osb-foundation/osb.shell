# Golden deviations: legacy transcripts vs the 1c shell

The phase-1c shell is a kernel-owned login/session rewrite. Four golden cases
(04-whoami, 12-login, 25-getchar, 27-ctrlc-order) were recaptured against the
1c shell; their pre-1c (legacy) transcripts are preserved in `legacy/`. Slice 2
recaptured 10-history for the same reason. This file records every differing
line and the reason, so a reviewer can see that the 1c/2 behavior is intentional
and not a regression.

Each entry lists the legacy transcript (`legacy/<case>.out`) and the current one
(`cases/<case>.out`).

## 04-whoami

- Legacy has an extra prompt line and a blank line before the first `WHOAMI`
  (`root@OSB A:\OSB\ROOT>` then blank then `root@OSB A:\OSB\ROOT>WHOAMI`).
- Current is tighter: `root@OSB A:\OSB\ROOT>WHOAMI` directly.
- Reason: driver/echo timing (the legacy capture picked up a duplicate prompt
  after login). The command output (`WHOAMI` -> `root`) is identical.

## 10-history

- Legacy has the extra prompt/blank duplication around each step: the LF that
  follows each CR is read as an empty command that reprints the prompt. Current
  is tight: one prompt per command, the next command echoing on that line.
- Reason: the 1c line reader swallows the LF that follows a CR (the `_skipLf`
  flag), so a CRLF is one Enter, not two. The HISTORY / `(:N)` / HISTORY N
  semantics are identical (all entries, recall, last-N, `No such history entry.`).

## 12-login

- Legacy has the same extra prompt/blank duplication around each step, and an
  extra `login: ` line after `LOGIN`/`LOGOUT`.
- Current has the tight sequence `LOGIN` -> `login: root` -> ... -> `LOGOUT` ->
  `login: `.
- Reason: same driver timing, plus the 1c `LOGIN`/`LOGOUT` re-prompt exactly
  once. The login/logout behavior is identical.

## 25-getchar

- Legacy starts with the phase-1b banner:
  `OSB Userspace Shell v0.72 (phase 1b)` / `Type EXIT to quit. Ctrl+C aborts a
  line.` and prompts `A:\OSB>`. The 1c shell has NO banner and requires login
  first: the transcript begins `login: root` / blank / `root@OSB A:\OSB\ROOT>`.
- Legacy prints a debug echo line `echo: <line>` after each entered line
  (old `Program.cs:55`, `PrintString("echo: ")`). The 1c line reader does not
  print `echo:`; it only echoes the typed characters at the prompt.
- The getchar behavior itself is unchanged: printable echo, backspace erases
  (`ABX^H ^HC`), Ctrl+C aborts a line, LF-only/CRLF/CR handling, and PS/2
  typing all match line for line once the login prelude is accounted for.

## 27-ctrlc-order

- Legacy starts with the phase-1b banner and `A:\OSB>` prompt; the 1c shell
  requires login first (`login: root` / blank / `root@OSB A:\OSB\ROOT>`).
- Legacy printed `echo: def`; the 1c shell does not (debug echo removed).
- The in-band Ctrl+C ordering is unchanged: `abc` + Ctrl+C aborts the `abc`
  line, then `def` runs as a new line, in that order.

## Cross-cutting: PS/2 lowercase departure

The ring-3 shell reads keys through `ConsoleInput`, which emits PS/2 letters in
LOWERCASE unless Shift XOR CapsLock is active (CapsLock 0x3A tracked; kernel
1c prelude P0.4). The legacy in-kernel `Keyboard` path was left untouched, so
its case behavior is not identical. The golden PS/2 cases (`key a`) therefore
exercise the new lowercase ConsoleInput behavior; a legacy capture that assumed
the old Keyboard case mapping is expected to differ in letter case.

## Not deviations (unchanged)

- The prompt shape `root@OSB A:\OSB\ROOT>` is the same in these four cases; the
  1c prompt is generally `<user>@OSB <cwd>>`, which for root in `A:\OSB\ROOT`
  renders identically.
- Case 26-spawnerr is unchanged; the flagged "duplicated args" was a misreading
  of the transcript, not a defect, and was left alone.
## Phase 3 (2.1 prelude): line-editor redraw and completion

### 11-completion (recaptured; spec, never legacy)

§1.4 records that the legacy in-kernel TAB completion triple-faults on any
non-empty token (`CmdCount` 56 vs 63 registered names), so completion could
never be captured from the legacy build; 11-completion is a `spec` case (see
MANIFEST), not a legacy parity fixture. Slice 2 captured it with the first
(`\r`-based) redraw. The phase-3 redraw is `\b`-based (wrap-aware), and a
single match whose typed prefix is already canonical only ECHOES the new
characters (§1.4 "extended in place") instead of redrawing. The case was
recaptured with `recapture.sh`; the completion results (HISTORY, `TRASH\`, the
`HELP  HALT  HISTORY` listing, the absolute/root/subdir paths, and the
no-match / over-long-token no-ops) are unchanged.

### Single-match case canonicalization (new behavior, no legacy equivalent)

The buffer always receives the real (canonical-case) name, so a
case-insensitive prefix (`tr` -> `TRASH`) executes correctly on the
case-sensitive ext4 tree. When the typed prefix is already canonical the screen
shows only the echoed suffix (`HI<TAB>` -> `HISTORY`), the legacy/§1.4 display
behavior. When the case differs the line is redrawn with the canonical spelling
(the legacy shell left the typed case on screen and in the buffer, so
`CD tRASH` then failed); this is the only user-visible difference.

### 31/32/33/34 (new spec cases, VGA goldens)

`31-edit-wrap`, `32-edit-history`, `33-complete-wrap`, `34-escape` are new spec
cases for the A1/A2/A4/A7 prelude work. They drive the shell over serial and
capture the 80x25 VGA text buffer plus the CRTC cursor (`vga` directive,
`cases/<name>.vga`) because cursor placement across the 80-column wrap is not
visible in the serial byte stream. Each of 31/32/33/35 was verified to FAIL
against the old `\r`-based redraw (both the serial and the VGA goldens differ),
which is the point of the capture.

### A1 serial-terminal limitation (`\b` wrap)

The wrap-aware redraw moves the cursor with `\b` (0x08), which the kernel VGA
console implements as a true cursor-left (column 0 steps to the previous row's
last column). On a real serial terminal the meaning of `\b` at column 0 is
terminal dependent (many wrap to the next line or ignore it), so the redraw is
only guaranteed on the kernel console and on the QEMU serial capture used by
the goldens. The serial byte stream is still the correct *character* sequence;
the limitation is cursor placement on third-party terminals.

### A5 legacy ambiguous-Tab listing (confirmed)

The legacy completion DOES list candidates on an ambiguous Tab:
`Completion.CompleteCommand` / `CompleteFile` (osb.kernel @ 5078f4b) print a
newline, then the matching names, then a newline, and redraw prompt + line;
they then extend the token to the longest common prefix. MIGRATION §1.4
already specifies exactly that ("Several matches: they are listed (two spaces
apart), the token is extended to the longest common prefix, prompt and line are
redrawn"). The userspace shell implements it; `11-completion` covers the
`H` -> `HELP  HALT  HISTORY` listing and `33-complete-wrap` covers the
`A:\OSB\` directory listing on a wrapped line.

### 35-edit-scroll (A1 scroll proof)

A new spec case: the prompt is at the bottom of a full screen, a 127-char line
wraps and scrolls once, then Home must land on the line start. Captured with a
VGA golden; it fails on the old `\r` redraw.
