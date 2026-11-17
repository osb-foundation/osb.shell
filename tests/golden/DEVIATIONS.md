# Golden deviations: legacy transcripts vs the 1c shell

The phase-1c shell is a kernel-owned login/session rewrite. Four golden cases
(04-whoami, 12-login, 25-getchar, 27-ctrlc-order) were recaptured against the
1c shell; their pre-1c (legacy) transcripts are preserved in `legacy/`. This
file records every differing line and the reason, so a reviewer can see that
the 1c behavior is intentional and not a regression.

Each entry lists the legacy transcript (`legacy/<case>.out`) and the current one
(`cases/<case>.out`).

## 04-whoami

- Legacy has an extra prompt line and a blank line before the first `WHOAMI`
  (`root@OSB A:\OSB\ROOT>` then blank then `root@OSB A:\OSB\ROOT>WHOAMI`).
- Current is tighter: `root@OSB A:\OSB\ROOT>WHOAMI` directly.
- Reason: driver/echo timing (the legacy capture picked up a duplicate prompt
  after login). The command output (`WHOAMI` -> `root`) is identical.

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