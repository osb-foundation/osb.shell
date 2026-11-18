# MIGRATION — legacy in-kernel shell → userspace shell

**Status: Phase 0 (architecture) approved. Phase 1a completed and verified. Phase 1b completed and verified. Phase 1c is **partially completed** — implemented and verified for identity syscalls (65h–69h), but the SUDO/END_SUDO elevation contract is broken in the current tree (see the status table below). Slices 2–3 (line editor/history/completion, environment 63h/64h, prompt, external dispatch over PATH) are present in the tree as pre-existing committed work. Remaining: slices 4–11 per §5; out of scope per §6.**

### Verified status table (audit 2026-11-18)

| Phase / slice | Status | Evidence |
|---|---|---|
| Phase 0 — architecture approved | **approved** | standing decisions §7; no code changes |
| Phase 1a — build/tooling | **completed** | osb.core 32ce84e, osbkt df5778e, osb.shell d7ed7cd, osb.kernel 749f6e9; all four `dotnet build` green; `./build.sh` + `./build.sh check` green; smoke PASS |
| Phase 1b — kernel hardening | **completed** | B1–B5 verified in source; golden 20/21/22/23/24/25/26/27 PASS; 24-ptr n=35 |
| Phase 1c — identity foundation | **partially completed** | 65h/66h/69h verified (28-identity PASS, 29-logout-kills PASS). **67h SUDO + 68h END_SUDO broken**: `Scheduler.EndSudo()` is a no-op (`Processes/Session.cs:136-139`) — the effective-uid restore was deleted by commit `1378692` (Slice 3). Consequence: **30-sudo-scope FAILS** ("TSUDO: FAIL childuid"); mutation **M06-endsudo-noop** cannot apply because the line it deletes is already gone. Root elevation never reverts. |
| Slice 2 — line editor / history / completion | **completed** | golden 10/11/31/32/33/34/35 PASS; host unit tests pass=52 fail=0 |
| Slice 3 — prompt / env / expansion | **completed** | golden 03/36/37 PASS; **but** Slice 3 also introduced the EndSudo regression above |
| Slices 4–11 | **pending** | no source changes beyond what is listed; golden cases at min-level > 3 are skipped |

Phase 0 — approved architectural decisions (standing):

* Layering `osb.shell -> osbkt -> INT 21h -> osb.kernel`; the shell calls only the public osbkt API.
* The kernel enforces security (pointer validation, filesystem permissions, identity, privileged operations, process ownership); Ring 3 is never trusted.
* `osbkt` owns `crt0.S`, the INT 21h syscall stub, `user.ld` and `mkosbx.py`.
* `osb.core` owns the canonical MiniCoreLib (single source file, linked into each freestanding program).
* No BCL convenience, no GC allocations, no exceptions for ordinary control flow; no source file > 400 lines.
* DOS personality preserved (drive letters, backslash paths, DOS commands/aliases/flags/messages).
* Legacy shell code is not deleted before behavior parity is tested and explicitly approved.

Phase 1a — completed and verified (build/tooling):

* `osbkt` public `Syscall` API + `osbkt/docs/ABI.md` v0; the shell compiles zero-alloc and checks `GETABI` at startup.
* `crt0.S`, the INT 21h syscall stub, `user.ld` and `mkosbx.py` live in `osbkt`; `osb.kernel/build.sh` consumes them (no private kernel copies).
* The osbkt dependency model: linked-source consumption of `osbkt`/`osb.core` is the verified model. A `ProjectReference` to `osbkt` was tested and is **incompatible** with the freestanding NoStdLib/MiniCoreLib arrangement (`csc` cannot import predefined types such as `System.Object` from a referenced NoStdLib library, and `osbkt` itself requires `MiniCoreLib` to compile `Syscall.cs`). Documented in `osb.shell/AGENTS.md` and `osb.shell/README.md`.
* QEMU serial smoke harness + `A11` golden-transcript capture.

Phase 1b — completed and verified (kernel hardening + userspace foundation):

* B1: full overflow-safe pointer validation (`IsUserRange`/`UserPtr`) on every pointer-taking syscall; `EFAULT` on a bad range, never a Ring 0 page fault.
* B2: `GETCHAR` (6Eh) cooked input merging serial + PS/2; returns `0..255` / `-1` empty / `-2` Ctrl+C (byte `0x03` never delivered); arrows as `ESC [ A/B`; raw `KEY` (52h) preserved for XWIN.
* B3: `SPAWNPATH` (6Ah) via the `SpawnReq` block (40 bytes); block/path/args/handle validation, exec check before task creation, 128-byte args, child PID or `-1/-3/-4/-5/-6`.
* B4: osbkt public `GetChar()` and `SpawnPath(...)` wrappers; all syscall numbers, register layout and request-structure details stay in osbkt; zero managed allocations.
* B5: existing syscalls `01h/09h/10h/20h-23h/30h/4Ch/80h-82h/40h-59h` unchanged; XWIN raw keyboard intact; `GETABI` unchanged.

Remaining phases and slices: Phase 1c is **partially completed** — 65h/66h/69h verified but 67h/68h broken (see the status table above). Slices 2–3 are present in the tree and verified passing. Slices 4–11 (filesystem writes, pipes, jobs, users, network, color, etc.) and the §6 out-of-scope items are NOT started by this task.

Decisions and amendments recorded in §7 are preserved unchanged.


> **1b housekeeping H-B (CONF/BOOT):** `A:\OSB\CONF\BOOT` (owner root, mode 0644)
> names the boot program; the kernel honors it **only** when built with
> `BOOT_FROM_CONF` (`build.sh` with `WITH_TESTS=1`). The default image always
> boots `A:\OSB\BIN\SHELL.EXE` regardless of `CONF/BOOT`. Verified by
> `tests/smoke.sh` (default image with a non-shell `CONF/BOOT` still boots
> SHELL.EXE). Documented in `osbkt/docs/ABI.md`.

Goal: move the *functionality* of the legacy in-kernel shell
(`osb.kernel/Shell/**`) into the userspace shell (`osb.shell`), so the kernel no
longer contains shell logic. The legacy shell is the **behavioral specification**,
not a code source: nothing is ported or copied as-is.

Target layering (non-negotiable):

```
osb.shell  ->  osbkt  ->  INT 21h  ->  osb.kernel
```

* The shell calls **only** the public osbkt API. Only osbkt knows register
  layout and syscall numbers.
* The kernel enforces **everything a malicious ring-3 program must not bypass**
  (credential checks, uid/mode checks, privileged operations).
* The shell owns presentation and interaction (editing, history, completion,
  prompt, parsing, expansion, dispatch, help).
* Every new syscall is documented in `osbkt/docs/ABI.md` **before**
  implementation. ABI.md v0 (every existing syscall, exactly as implemented)
  exists since Phase 1a; each slice adds its entries first and bumps the
  GETABI version when the table changes.
* OSB stays DOS-like: drive letters, `\`, DOS command names/aliases/flags/messages.

---

## 1. (a) Behavior inventory of the legacy shell

Source of truth: `osb.kernel/Shell/**` as it exists today. Quoted strings are
the exact console output (`\n` line endings on VGA; serial mirrors with CRLF).

### 1.1 Boot, login and sessions

* Boot path today (`osb.kernel/Kernel.cs:63-80`): `Environment.Init()` →
  `Scheduler.SpawnFile("A:\OSB\BIN\SHELL.EXE")` → `WaitFor(pid)` → **halt**.
  The legacy `Shell.Run()` is **not called anymore** (no caller remains), so
  the userspace shell is the only shell. Consequence: `Users.Init()`,
  `FileCommands.LoadColor()` and the login loop currently run **only** inside
  the unreachable legacy code — the live boot path has no login and no
  PASSWD/SUDOERS initialisation. The migration must re-home all of it.
* Legacy session model (the spec):
  1. `login: ` prompt. Name lookup is case-insensitive (`Users.Find`).
     Unknown name or wrong password → `Login incorrect`, loop repeats.
     Accounts with an empty password field log in with no prompt.
     Accounts with a password get `Password: ` (no echo, 32 chars max).
     Ctrl+C at the login prompt just re-prompts.
  2. On success: session user set; `$USER` = name; `$HOME` = home
     (force-set, **read-only** for `SET`); current directory = home, or
     `A:\` when the home folder cannot be resolved.
  3. ext4 mounted, `$HOME\TRASH` and `$HOME` created for every account at
     boot (`Users.Init` → `TrashCommands.EnsureAll`, running as root),
     `A:\OSB\TEMP` forced to mode 0777, `A:\OSB\CONF\COLOR.CFG` applied.
  4. `EXIT` / `LOGOUT` end the session → back to `login: ` (logout sets the
     session user to "nobody"; env `$USER`/`$HOME` are **not** reset).

### 1.2 Console input and line editing

* Two input rings are folded into one editing path: COM1 serial (IRQ4) and
  PS/2 keyboard (IRQ1). Serial bytes are literal ASCII; Ctrl+C on serial
  (0x03) and Ctrl+C on PS/2 become a console-control event, not a character.
* Session line editor (`Shell.Session`): 128-char line, printable 0x20–0x7E,
  Backspace (0x0E / DEL) erases with `\b \b`; Enter runs the line.
* Arrow keys: Up/Down walk history (see 1.3). Serial arrows arrive as
  `ESC [ A/B` and `ESC O A/B` (xterm application mode); keyboard arrows as
  extended scancodes `0xE0 0x48/0x50`. Left/Right, Home/End, Delete: not
  handled (ignored).
* TAB runs completion (1.4). Ctrl+C mid-line: line discarded, fresh prompt
  (history navigation reset).
* Login/password line reader: same editing minus history/arrows/TAB, 32-char
  cap, echo flag (off for passwords), Ctrl+C re-prompts.
* Shift state is tracked from the keyboard only (`0x2A/0x36` make, `0xAA/0xB6`
  break); serial is always unshifted ASCII.

### 1.3 History and recall

* Fixed ring: 256 entries × 128 chars (kernel `Layout.MaxHistory`).
  Empty lines and consecutive duplicates are not recorded.
* `HISTORY [n]` prints the last *n* entries (default 100) as `%5d  line`,
  numbered by monotonic push order (1-based); `History is empty.` when none.
* `(:N)` re-runs entry N: echoed, re-recorded (a broken recall prints
  `No such history entry.` and is **not** recorded).
* Up/Down navigate the ring while editing; the draft being edited is kept and
  restored by Down past the newest entry; navigation resets after Enter or
  Ctrl+C.

### 1.4 TAB completion

* First token of the line → command names (case-insensitive; fixed table of
  command names mirroring the dispatch, `Completion.cs:336`).
* Token starting with `/` → flags of that command. Today only `LN`/`LNK /S`.
* Otherwise → file/directory name completion against ext4 (drive A:):
  relative to the current directory, `dir\name`, `\name`, `A:\dir\name`;
  case-insensitive prefix match, dot-entries skipped.
* Single match: the token is extended in place; a directory gets a trailing
  `\`. Several matches: they are listed (two spaces apart), the token is
  extended to the longest common prefix, prompt and line are redrawn.
  No match: nothing happens (no beep, no message).

**Input-path finding (H2): Tab is device-independent in the legacy code, but
completion is broken in the legacy build.** Serial 0x09 and PS/2 Tab reach the
same handler: serial `Shell.cs:184` `Serial.Pop()` → `Shell.cs:207`
`FeedChar((char)ch, …)` → `Shell.cs:287-290` `if (c == '\t')
Completion.Complete(...)`; PS/2 scancode 0x0F → `Shell.cs:232`
`FeedChar('\t', …)`. There is **no serial-specific gate** — serial Tab is not
missing; it is the same path as the keyboard. However the legacy table is
oversubscribed: `Completion.cs:28` declares `CmdCount = 56` while
`InitCommands` (`Completion.cs:336-355`) registers **63** names, a 7-entry
overflow of the fixed `CmdTable` (`Completion.cs:30-35`) that corrupts the
`lens[]` array and makes `NameMatches` (`Completion.cs:113-120`) read out of
bounds → triple fault on **any non-empty token** (empty-line Tab is safe
because `CompleteCommand` returns early at `if (tl == 0) return;`). Verified:
bumping `CmdCount` to 64 makes "AB"+Tab complete to `ABOUT`. Consequence for
the migration: completion **cannot be golden-captured from the legacy build**
(case 11-completion is a `spec` golden captured from the new shell instead —
see the 2.1 prelude note in §5), and the new shell must
implement completion correctly. The driver's PS/2 path (QEMU monitor
`sendkey`, H2) exercises Tab/arrows/Ctrl+C on the virtual keyboard, so the new
implementation can be tested on both devices.

### 1.5 Prompt rendering ($PROMPT)

Default `PROMPT=$USER@OSB $P$G`. Tokens (case-insensitive): `$P` cwd,
`$N` drive, `$G` `>`, `$L` `<`, `$B` `|`, `$Q` `=`, `$S` space, `$D` date,
`$T` time, `$V` `OSB 0.1`, `$H` backspace, `$E` ESC, `$_` newline, `$$`
literal `$`; any other `$NAME` expands the environment variable (undefined
names are printed literally as `$NAME`). Default when unset: `$P$G`.

### 1.6 Environment and `$VAR` expansion

* Session store, names upper-cased (case-insensitive), boot defaults:
  `PATH=A:\OSB\BIN`, `PROMPT=$USER@OSB $P$G`, `USER=guest`, `HOME=A:\OSB\ROOT`,
  `TEMP=A:\OSB\TEMP`, `COMSPEC=A:\OSB\BIN\COMMAND.COM`, `OS=OSB`, `VER=0.1.0`,
  `LASTDRIVE=Z`. `HOME` is locked (read-only for `SET`).
* `SET` lists all; `SET NAME` lists one (prefix match actually — `ListPrefix`);
  `SET NAME=value` sets; failure → `Cannot set: read-only or out of memory.`
* Every command line is `$VAR`-expanded **before** dispatch; undefined names
  round-trip literally (so `SET PROMPT=$P$G` keeps its tokens).
* `SET NAME=` sets an empty value (does not delete).

### 1.7 Command dispatch, aliases, per-command help

* Case-insensitive; first match wins; a line that matches nothing internal is
  treated as an external program (1.8), else `Bad command or file name.`
* `/?` or `-?` as any argument of a **known internal** command prints that
  command's help page and nothing runs. Unknown commands fall through and
  receive the flag themselves. `HELP` alone prints the overview (multi-line
  command list); `HELP <cmd>` prints the same page as `/?`;
  unknown → `No help for <cmd>`.
* Exact command/alias table (dispatch in `Shell.cs:441-647`):

| Command | Aliases | Notes |
|---|---|---|
| VER / ABOUT | — | static text |
| HELP [cmd] | — | overview or page |
| CLS | CLEAR | clear screen (VGA + serial `ESC[2J ESC[H`) |
| MEM / HALT / POWEROFF / REBOOT | POWEROFF: SHUTDOWN, QUIT | machine control |
| EXIT / LOGOUT | each other | end session → `login:` |
| TIME / DATE / TICKS / SLEEP <s> / ECHO <text> | — | clock & text |
| SET [NAME[=value]] | — | environment |
| DIR [path] [/W] / CD [path] / PWD / TYPE <file> / SIZE <file> | — | read |
| MD / RD | MKDIR, RMDIR, RDDR | write |
| DEL | ERASE, DELETE | to trash |
| REN / MOVE | RENAME | move = rename (files across dirs, dirs same-dir) |
| COPY | CP | single file, no wildcards |
| LN [/S] / COLOR [fg [bg]] | LNK | link / palette |
| TRASH [/F pat \| /U name \| pat] | — | trash list/search/undelete |
| GREP [/I] pat [file] | — | line filter |
| USER [/ADD/CHANGE/DEL/SUDO/UNSUDO ...] / LOGIN [name [pass]] | — | accounts |
| CHOWN <user> <path> / CHMOD <octal> <path> / SUDO <cmd> | — | ownership |
| WHOAMI / HISTORY [n] | — | identity/history |
| SPAWN [BAD\|SPAWNER] [&] | RING3 | built-in ring-3 programs |
| DEMO [&] / TASK / KILL <pid> / WAIT <pid> / TAB <pid> | TASK: PS | jobs |
| OSLANG <script.OSL> [args] | — | embedded interpreter |
| KISS [file] | — | runs `OSLANG A:\OSB\BIN\KISS.OSL [file]` |
| NET [ip\|DHCP\|AUTO] / PING <ip> [n] / PCI [net\|scan\|B:D.F] | NET: IFCONFIG | network |

#### 1.7a Coverage table (H4): every 1.7 command → golden case

Cases live in `osb.shell/tests/golden/cases/`; the level each needs is in
`tests/golden/MANIFEST`. "NO GOLDEN YET" = deterministic but not yet captured
(reason given), or not capturable from the legacy build.

| Command / syntax (aliases) | Golden case | Notes |
|---|---|---|
| VER / ABOUT | `01-ver` | static text |
| HELP [cmd], `/?` | `09-help` | overview + per-command page |
| CLS (CLEAR) | `07-cls` | VGA + serial clear |
| MEM | NO GOLDEN YET | deterministic (memory counters); not in the H4 list |
| HALT / POWEROFF (SHUTDOWN, QUIT) / REBOOT | NO GOLDEN | machine control halts/reboots QEMU; HALT dropped (decision 6) |
| EXIT / LOGOUT | `12-login` | LOGOUT → `login:` |
| ECHO <text> | `02-echo` | `$` and long-line handling in `23-print` (1b) |
| TIME / DATE / TICKS / SLEEP <s> | NO GOLDEN YET | clock output is wall-clock volatile |
| SET [NAME[=value]] | `03-set` | env get/set/list |
| DIR [path] [/W] | `05-dir` | listing + date mask |
| CD [path] / PWD | `06-cd` | navigation |
| TYPE <file> / SIZE <file> | NO GOLDEN YET | TYPE error in `14-fileops`; success path not captured |
| MD / RD (MKDIR, RMDIR, RDDR) | `14-fileops` | create/remove |
| DEL (ERASE, DELETE) | `14-fileops` | to trash |
| REN / MOVE (RENAME) | `14-fileops`, `14b-fileerr` | success + errors |
| COPY (CP) | `14-fileops`, `14b-fileerr` | success + errors |
| LN [/S] (LNK) | `14b-fileerr` | errors; success path NO GOLDEN YET |
| COLOR [fg [bg]] | `18-color` | table + usage + invalid + same fg/bg |
| TRASH [/F pat \| /U name \| pat] | `08-trash` | list/search/undelete |
| GREP [/I] pat [file] | `15-pipes` | via pipes |
| USER [/ADD/CHANGE/DEL/SUDO/UNSUDO] | `13-users`, `13b-nonroot` | add/del/list; non-root denials |
| LOGIN [name [pass]] | `12-login`, `13b-nonroot` | login: flow + password |
| CHOWN <user> <path> / CHMOD <octal> <path> | `13b-nonroot` | non-root denials; success NO GOLDEN YET |
| SUDO <cmd> | `13b-nonroot` | non-sudoer refusal; success NO GOLDEN YET |
| WHOAMI | `04-whoami` | identity |
| HISTORY [n], `(:N)` | `10-history` | list + recall + "No such history entry." |
| SPAWN [BAD\|SPAWNER] [&] (RING3) | NO GOLDEN YET | foreground/child ordering races; covered by `20-spawn` (1b) |
| SPAWNPATH (6Ah) + WAIT exit code | `21-exit7` | 1a.1 minimal form: TWait.EXE SPAWNPATHs TExit.EXE, WAIT returns 7 |
| DEMO [&] / TASK (PS) / KILL <pid> / WAIT <pid> | `16-jobs` | task list, ctrl-c abort, kill/wait errors |
| TAB <pid> | NO GOLDEN YET | needs a background job to foreground; no deterministic case |
| OSLANG <script.OSL> [args] / KISS [file] | NO GOLDEN | out of scope (A12); OSLANG stays in kernel |
| NET [ip\|DHCP\|AUTO] (IFCONFIG) | `17-net` | interface info |
| PING <ip> [n] | NO GOLDEN YET | round-trip timing is non-deterministic |
| PCI [net\|scan\|B:D.F] | `19-pci` | bus list + device detail |
| TAB completion (key) | NO GOLDEN YET | legacy completion crashes (1.4 overflow); driver has PS/2 `key` path |

### 1.8 External programs

* First token = program name, remainder = raw argument string (max 92 chars
  used). `.\name` or `./name` runs `name` relative to the current directory
  (must resolve). Otherwise each `;`-separated `$PATH` entry is tried as
  `<entry>\<name>` and `<entry>\<name>.EXE`.
* Start = load OSBEXE (`OSBX` header) from ext4, new address space, pass args
  (retrievable via `$GETARGS`), name = file base name. Without trailing `&`
  the shell waits; with `&` the prompt returns immediately.
* Spawn failures: `Bad command or file name.` (not found),
  `Not a valid executable.`, `No free task slot.`, `Out of memory.`

### 1.9 Pipes (`A | B`)

* The line is split on `|`; every stage but the last runs with its console
  output **captured**; the next stage's command line gets `\n` + captured
  text appended after its arguments (GREP splits its arguments at the first
  `\n`). The last stage prints normally. Pipelines always run in the
  foreground (a trailing `&` is ignored while piping).
* Capture buffer: 4096 bytes, overflow dropped silently. While capturing,
  `CLS` clears nothing (`Vga.Clear()` is a no-op in capture mode).
* This is a ring-0 mechanism (VGA driver capture flag): a ring-3 shell
  **cannot** capture another process's output this way — see design choice 3.1.

### 1.10 Background `&` and foreground control

* Trailing `&` (after trimming spaces) = background: prompt returns at once.
* Foreground: prints `Foreground [pid]  (Ctrl+C returns to the shell)`, waits
  for the job; on Ctrl+C: `Foreground job aborted (Ctrl+C).`; on zombie:
  `Job finished, exit code N.`; else `Job ended.`
* `SPAWN`/`DEMO` messages: `Spawned HELLO [pid] in the foreground (block 0x…).`
  / `Started DEMO [pid] in the background.`

### 1.11 Command behavior details (messages + permission checks)

Permission model (legacy, `Users.cs`): uid 0 (root) bypasses everything;
otherwise owner bits, then other bits (`CanRead` 0400/0004, `CanWrite`
0200/0002). "Parent writable" (write+execute on the directory inode) guards
creation/removal of entries. **All of these checks live in the shell command
code today — the syscalls do not check anything.**

| Command | Key messages (exact) | Permission checks (legacy) |
|---|---|---|
| DIR [path] [/W] | `Path not found`, `Error reading inode`, `Not a directory`, `Permission denied` | read on the directory inode; long format: perms, links, owner, group, size, `MM/DD/YYYY HH:MM`, name, symlink ` -> target` (cyan); `/W` names + `\t` |
| CD [path] | `Invalid directory`, `OK` (CD alone prints cwd) | **none** (deliberate: no leak, since every consumer re-checks) |
| PWD | prints cwd | — |
| TYPE <file> | `TYPE [file]`, `File not found`, `Error`, `Not a file`, `Permission denied` | read on file; output stops at a NUL byte within each 4 KiB block |
| SIZE <file> | `SIZE [file]`, `File not found`, `Error`, `Directory.`, `Size: N bytes` | **none** |
| MD/MKDIR | `MD [directory]`, `Path not found`, `Permission denied`, `Already exists`, `No free inode`, `No free block`, `Error adding entry`, `OK` | parent writable; new dir mode 0755 owned by session uid |
| RD/RMDIR/RDDR | `RD [directory]`, `Path not found`, `Permission denied`, `Not found`, `Not a directory`, `Directory not empty`, `Error removing entry`, `OK` | parent writable; must be empty |
| DEL/ERASE/DELETE | `DEL [file]`, `Usage: DEL [file]`, `Path not found`, `Permission denied`, `File not found`, `Is a directory; use RD.`, `OK` | parent writable; file moved to `$HOME\TRASH` (name, `name~1`…, origin recorded in `$HOME\TRASH\.ORIG` as `name<TAB>canonical-path`); hard delete only when the trash cannot be used |
| REN/RENAME/MOVE | `REN old new`, `Path not found`, `Permission denied`, `Not found`, `Target already exists`, `Cannot move directory across dirs`, `No free inode`, `Error`, `OK` | parent writable (both sides); files only across dirs |
| COPY/CP | `COPY src dst`, `Source not found`, `Cannot copy directory`, `Permission denied`, `Target already exists`, `No free inode`, `Error`, `OK` | read on source; write on destination dir (or parent writable) |
| LN/LNK [/S] | `LN [/S] source destination`, `LN /S target destination`, `Source not found`, `Cannot hardlink directory`, `Target already exists`, `Permission denied`, `Cannot create symlink`, `Error`, `OK` | parent writable; hardlink (link count +1) or symlink (target string stored) |
| COLOR [fg [bg]] | table + `Usage: COLOR fg bg`, `Invalid color (0-15).`, `Foreground and background cannot be the same.`, `Color set. Saved to A:\OSB\CONF\COLOR.CFG`, `Color set (not saved: permission denied).`, `Color set.` | values 0–15; persistence needs write on `A:\OSB\CONF`, else session-only |
| TRASH | `(empty)`, `Trash is empty`, `Not in trash`, `No home directory`, `No filesystem`, `Permission denied`, `No free name`, `Error restoring`, `Usage: TRASH /U <name>`, `OK`; list rows `name  size  was: path` | read on trash dir; undelete needs write on the restore parent (falls back to cwd when the original folder is gone; picks `name~1`… on collision) |
| GREP [/I] pat [file] | `Usage: GREP [/I] <pattern> [file]`, `File not found`, `Not a file`, `(empty)` | **none today** (reads any readable-by-path file — a parity gap to fix kernel-side) |
| USER | header `  NAME          UID   HOME … FLAGS`, `Unknown USER flag. …` | list: none; `/ADD`,`/DEL`,`/SUDO`,`/UNSUDO`: root only (`Permission denied`); `/CHANGE`: root or own password (`New password: ` prompt, no echo); messages `User already exists`, `Invalid name (…)`, `No such user`, `Cannot delete root`, `Cannot add user`, `User added, but the home folder could not be created`, `Home not removed (not empty or missing)`, `Password changed`, `Cannot change password`, `OK`, `Error` |
| LOGIN [name [pass]] | `Login incorrect`, `Password: ` | anyone; switches the session (incl. `$USER`/`$HOME`/cwd) |
| CHOWN | `Usage: CHOWN <user> <path>`, `No such user`, `OK`, `Path not found` | root only |
| CHMOD | `Usage: CHMOD <octal> <path>`, `Invalid mode (octal digits)`, `Operation not permitted`, `OK`, `Path not found` | root or file owner |
| SUDO <cmd> | `Usage: SUDO <command>`, `User is not in the sudoers file.`, `Password: `, `Sorry, try again.` | root: runs directly; sudoers member: own password, then command runs as root (session temporarily root) |
| WHOAMI | prints session user name | — |
| SET | `Cannot set: read-only or out of memory.`, `Environment variable not defined.` (prefix listing) | `$HOME` locked; env is global session state |
| HISTORY [n] | `History is empty.` | — |
| SPAWN/RING3 | `Spawned HELLO [pid] …`, `No free task slot.`, `Out of memory.` | **none** (any user may spawn; BAD = crash test program) |
| DEMO [&] | `Started DEMO [pid] …` | none |
| TASK/PS | header ` PID  NAME        STATE    MEMORY      ROLE` | none (shows all tasks incl. kernel) |
| KILL <pid> | `No such process.`, `Cannot kill the console.`, `Killed pid N.` | **none** (any user may kill any task) |
| WAIT <pid> | `Usage: WAIT <pid>`, `Waiting for pid N ...`, `Process N exited, code C.`, `No such process (or already gone).` | none (reaps any pid) |
| TAB <pid> | `No such process.`, `The console is already in the foreground.`, `Process is not running.` | none |
| MEM | `Physical frames: N used / M total (K KiB usable)`, `Range: 0x… - 0x…` | — |
| TIME/DATE/TICKS | `Uptime: N s (M ticks)` | — |
| SLEEP <s> | `Sleeping ...`, `Done.` | — |
| ECHO <text> | prints text | — |
| HALT | `System halted. (…)` + CPU stop | **none** |
| POWEROFF/SHUTDOWN/QUIT | `Powering off (ACPI S5) ...` | **none** |
| REBOOT | `Rebooting ...` | **none** |
| VER / ABOUT | `OSB (dotnet) version 0.1 - bootable kernel` / `OSB: Operating System Basic, created in 2000, rebooted in C#.` | — |
| HELP | overview (18 lines, `SystemCommands.Overview`) | — |
| NET/IFCONFIG | `adapter …`, `address … netmask …`, `gateway … dns …`, `source …`; `DHCP: lease obtained.`, `DHCP: no response from a server (no offer)/(offer, no acknowledgement)/(out of memory)`, `address set to X`, `adapter     not started yet (PCI net for hardware detail)` | none (hardware state, single console) |
| PING | `Usage: PING <host> [count]`, `PING: cannot resolve host X`, `PING: no network interface (error N)`, per probe `unreachable` / `reply from IP: seq=N time=Nms` / `Request timed out.` | none |
| PCI | list rows; `PCI net`; `PCI B:D.F` detail (BARs, IRQ) | none |
| OSLANG / KISS | interpreter runs the script (`OSL.CONSOLE/FILE/APP` built-ins) | none (runs as the session user) |

---

## 2. (b) Classification

**A = pure userspace logic** (moves into osb.shell on top of existing or new
syscalls). **B = userspace + new/extended syscalls** (kernel provides a
primitive; shell keeps the behavior). **C = must stay kernel-side**
(reason given). Every command's *presentation* is A; the *effect* determines
the class.

| Item | Class | Rationale |
|---|---|---|
| Line editor, history ring, `(:N)`, HISTORY cmd, Up/Down | A | presentation; history is per-shell state |
| TAB completion (commands/flags) | A | shell tables |
| TAB file completion | B | needs readdir/stat primitives (21h exists; needs permission-checked variant + stat-by-ino) |
| Prompt rendering ($P$G…), `$VAR` expansion in the line | A | presentation (reads $PROMPT/$VAR via osbkt) |
| SET / environment store | B | per-session kernel env (children must inherit DOS-style) → GETENV/SETENV syscalls |
| Dispatch, aliases, `/?`, HELP pages | A | text + tables |
| External program launch, PATH search, args | B | needs SPAWN-by-path with args syscall; PATH search itself stays in the shell |
| Background `&`, foreground wait, Ctrl+C abort, exit codes | B | WAIT/SPAWN exist (80h/81h); needs spawn-by-path; job messages are A |
| TASK/PS listing, KILL, TAB(pid) | B | needs list/kill/setfg syscalls with permission rules |
| DEMO | C | kernel test task (scheduler fixture); expose only if desired as built-in SPAWN id |
| SPAWN/RING3 (HELLO/BAD/SPAWNER built-ins) | C | in-kernel ring-3 test blobs; shell just triggers them |
| DIR/CD/PWD/TYPE/SIZE | B | resolve/dirnext/getcwd exist but unchecked; CD needs CHDIR; TYPE needs READ; SIZE needs stat (20h suffices) |
| MD/RD | B | needs MKDIR/RMDIR syscalls with parent-write enforcement |
| DEL (trash) | A + B | trash bookkeeping is shell logic on top of MOVE/READ/WRITE/CREATE syscalls; each primitive is permission-checked kernel-side |
| TRASH list/search/undelete | A + B | same: shell logic over checked syscalls |
| REN/RENAME/MOVE, COPY/CP, LN/LNK | B | RENAME/COPY/LINK syscalls (or COPY composed from OPEN/READ/WRITE in the shell — see 3.9); permission checks kernel-side |
| GREP | A | reads via file/pipe input primitives |
| COLOR + COLOR.CFG persistence + login-time load | B (minor) | console attr syscalls (GET/SETCOLOR); file side via normal file syscalls; load at login is shell logic |
| CLS/CLEAR | B | console clear syscall (ring 3 cannot touch 0xB8000) |
| Console output (01h/09h) | B (exists) | keep; extend for per-task stdout redirection |
| Cooked console input (keys + serial + Ctrl+C + arrows) | B | new GETCHAR-class syscall (kernel owns both IRQ rings and the scancode map) |
| Login/logout, $USER/$HOME, PASSWD/SUDOERS | C + B | credentials, sudoers and session identity **must** be kernel-enforced (login syscall verifies; session uid lives in the kernel); the prompts are A |
| WHOAMI | B | reads session identity from the kernel |
| USER /ADD /CHANGE /DEL /SUDO /UNSUDO | B | account mutations are privileged syscalls (root-checked); list is a query syscall; all prompts/messages are A |
| CHOWN / CHMOD | B | privileged syscalls with owner/root checks (today unchecked in ring 0!) |
| SUDO | B | elevation syscall (kernel verifies password + sudoers); command dispatch stays in the shell |
| PERMISSION ENFORCEMENT for every file operation | C | uid/mode checks move **into** each file syscall (today: shell-only, see blocker X7) |
| POWEROFF / REBOOT | C | hardware privilege + must require root (today callable by any ring-3 task!) |
| HALT | C (or drop) | CPU stop; see 3.8 |
| MEM | B (exists, 10h) | keep |
| TIME / DATE / TICKS / SLEEP | B (54h/55h/56h exist; DATE needs adding) | keep + extend |
| Pipes `A \| B` | B | needs stdout redirection (3.1); pipeline parsing stays in the shell |
| NET / PING / PCI | C (exposed via B syscalls) | hardware drivers stay in the kernel; syscalls: net config/info, one-shot ping, PCI enumerate/detail |
| OSLANG interpreter | C (stays) / out of scope | see §6 — becomes OSLANG.EXE later |
| KISS | out of scope | OSLANG script launcher |
| GFX desktop (Shell/Desktop*.cs) | out of scope | replaced by osb.xwin XWIN.EXE; currently unreachable from dispatch |
| Session identity per task/console, open-file table, stdout target | C | kernel state |

---

## 3. (c) ABI and osbkt API proposals

Numbering continues the existing DOS-style map. **No number below is final
until its ABI.md entry exists** (ABI.md is written first, per slice). Existing
numbers are kept for compatibility (XWIN.EXE already uses 01h/4Ch/58h/59h and
the 40h–59h range).

### 3.1 Proposed syscall map (additions)

Error returns (A7): `-1` (EFAULT) and `-2` (ENOSYS) keep their **existing**
meaning and are never redefined. **`-2` is deliberately overloaded**: it means
ENOSYS as a syscall error *and* Ctrl+C as GETCHAR's return value (GETCHAR has
no "error" space of its own — `-1` = empty, `-2` = Ctrl+C, D2). Callers must
not treat a GETCHAR `-2` as "syscall missing"; the distinction is per-syscall
and documented in each ABI.md entry. New error codes are defined once in
`osbkt/docs/ABI.md` (v0 records the existing behavior; later slices extend the
table): `-3` EACCES (permission denied), `-4` ENOENT (not found), `-5` EINVAL
(not empty / not a directory / bad type / bad mode), `-6` ENOMEM (out of
memory / no task slot / no handle), `-7` EINTR (interrupted by Ctrl+C — WAIT,
D6), `-8` EBADF (handle not open for this operation — **including READ on a
console handle, D9**: console READ returns -8, never -1 which is EFAULT).
Per-syscall exceptions are listed in each ABI.md entry (e.g. GETCHAR: `-1` =
empty, `-2` = Ctrl+C — its own return space, not an error). All pointer
arguments must be validated **including range end** (`ptr ≥ UserBase && ptr+len
≤ UserLimit`) — today's gate only checks the start, which lets ring 3 panic the
kernel (blocker X9).

**ABI/version lockstep (E3).** `osbkt/docs/ABI.md` and `Syscall.AbiVersion`
are one contract: every slice that adds, removes, or changes a syscall-table
entry bumps **both** in the same commit (ABI.md gets the new entry and the new
version number; `Syscall.AbiVersion` is set to that number). The shell refuses
to run when the kernel's GETABI (82h) is **lower** than the version it was
built against: at startup it calls GETABI and, if `kernelAbi <
Syscall.AbiVersion`, prints a clear "shell needs ABI vN, kernel is vM" message
and exits instead of calling syscalls the kernel does not implement. A *higher*
kernel ABI is accepted (forward-compatible: newer kernel, older shell).

| AH | Name | Args | Returns | Enforcement |
|---|---|---|---|---|
| 24h | CHDIR | RDI=path ptr, RSI=len | 0 / neg. err | resolve; **no permission check (legacy parity, decision 2)** |
| 25h | MKDIR | path, mode in RDX (default 0755) | 0 / err | parent write |
| 26h | RMDIR | path | 0 / err | parent write + empty |
| 27h | UNLINK | path | 0 / err | parent write (used by trash fallback) |
| 28h | RENAME | old ptr/len, new ptr/len | 0 / err | write on both parents; files across dirs |
| 29h | LINK | src path, dst path | 0 / err | parent write; optional /S via flags in RDX → symlink target = src string |
| 2Ah | OPEN | path ptr, RSI=len, RDX=flags (bit0 create+trunc, bit1 append) | handle 3–7 / neg. err | read → CanRead; write/create → parent write; O_CREAT owner=task uid, mode 0644 |
| 2Bh | READ | RDI=handle, RSI=buf, RDX=len | n / neg. err | handle table; buffer fully range-checked; handle 0 = stdin (pipes); **READ on a console handle returns -8 (EBADF, D9) — never -1 (EFAULT)** |
| 2Ch | WRITE | handle, buf, len | n / neg. err | handle opened for write; handle 1 = stdout (console or file) |
| 2Dh | CLOSE | handle | 0 / err | — |
| 2Eh | LSEEK | handle, off (RDX), whence (RCX: 0 set/1 cur/2 end) | new off / neg. err | — |
| 2Fh | CHMOD | path, mode in RDX | 0 / err | root or owner |
| 60h | STAT | ino (RDI), out 32B (RSI) | 0 / neg. err | **metadata-only, NO read-permission check (A2: DIR must list entries of readable dirs even when the file itself is unreadable)**; returns mode/uid/gid/links/size/mtime |
| 61h | CHOWN | path, uid in RDX | 0 / err | root only |
| 62h | READLINK | path, buf, cap | n / neg. err | read on the link inode |
| 63h | GETENV | name ptr/len, buf, cap | n / 0 missing | **per-task env** (copied at spawn, decision 3) |
| 64h | SETENV | name ptr/len, val ptr/len | 0 / err | per-task env; kernel-locked names (HOME/USER after login) refused |
| 65h | LOGIN | name ptr/len, pass ptr/len | 0 / -1 bad | kernel verifies PASSWD; **passwordless only at initial boot login (A3)**; sets console-session uid + caller task uid |
| 66h | LOGOUT | — | 0 | console session uid → none; caller task uid → none |
| 67h | SUDO | pass ptr/len | 0 / -1 | kernel checks SUDOERS + password; elevates **calling pid only** (A4), restored by 68h |
| 68h | END_SUDO | — | 0 | restore pre-sudo uid of the calling pid |
| 69h | WHOAMI | buf, cap | n | session user name (empty when logged out) |
| 6Ah | SPAWNPATH | **1a.1 minimal form (shipped):** RDI=path, RSI=pathLen, RDX=args, RCX=argsLen. The `SpawnReq*` 7-field form (3.1a) is phase 1b | pid / neg. err | 1a.1: `Scheduler.SpawnFile` + exec check (any x bit for task uid, root always); stdin/stdout = console (no handle redirection yet). **D7 deviation: legacy had NO exec check — SPAWNPATH requires an x bit; `build.sh` stages every `.EXE` with mode 0100755 so all shipped programs pass** |
| 6Bh | TASKLIST | out buf, cap | n entries | **all tasks visible (decision 4)** |
| 6Ch | KILL | pid | 0 / err | same uid or root; never the console/session leader |
| 6Dh | SETFG | pid | 0 / err | same uid or root |
| 6Eh | GETCHAR | — | char 0–255 / -1 empty / -2 Ctrl+C | merges serial+PS/2, arrows as `ESC[A/B`, see 3.7; **-1/-2 are GETCHAR's return space (A7); Ctrl+C is consumed kernel-side — byte 0x03 is never delivered (D2)** |
| 6Fh | CLEAR | — | 0 | console clear + serial `ESC[2J H` |
| 70h | SETCOLOR | fg (AL), bg (AH) | 0 / err | console attribute |
| 71h | GETCOLOR | out: fg, bg | 0 | — |
| 72h | DATETIME | out 8B (Y,M,D,h,m,s + pad) | 0 | RTC |
| 74h | NETINFO | out: ip/mask/gw/dns/mac/src | 0 | — |
| 75h | NETSET | ip, mask, gw (RCX) | 0 | root only |
| 76h | NETDHCP | out: ip/mask/gw/dns | 0 / -1 | root only |
| 77h | PING1 | ip (RDI), seq (RSI), out rtt ms (RDX) | 1 reply / 0 timeout / -1 unreachable | root only (raw send) |
| 78h | PCIENUM | out records, cap | n | — |
| 79h | PCIDETAIL | bus/slot/func, out | 0 | — |
| 82h | GETABI | — | ABI version number | none (A8; osbkt checks at startup) |
| 58h/59h | POWEROFF/REBOOT (exist) | — | — | **add: root only (decision 5)** |
| 80h/81h | SPAWN/WAIT (exist) | — | — | keep for built-ins; SPAWNPATH supersedes for files |

#### 3.1a `SpawnReq` — the 6Ah argument block (E1)

> **1a.1 note (R8):** the **minimal** 6Ah form (RDI=path, RSI=pathLen, RDX=args,
> RCX=argsLen) is what actually shipped in 1a.1; this 7-field `SpawnReq` block
> is the **phase-1b** target, where the handle-based redirection (A5) lands.

6Ah has **seven** inputs but the gate passes only four register args
(RDI/RSI/RDX/RCX), so they travel as one pointer to a caller-filled block
(RDI = `SpawnReq*`). The kernel copies and range-checks the block before use;
ring 3 must not keep writing it after the call.

```c
// All pointers are user-window addresses (UserBase..UserLimit). All lengths
// are bytes and must satisfy ptr >= UserBase && ptr + len <= UserLimit.
typedef struct SpawnReq {
    uint32_t pathLen;      // +0   length of the path string (<= 260)
    uint32_t argsLen;      // +4   length of the raw argument string (<= 128, decision 7)
    uint32_t reserved;     // +8   must be 0 (future flags; kernel rejects != 0)
    uint32_t stdoutH;      // +12  caller's write handle to dup into child fd 1, or 0xFFFFFFFF
    uint32_t stdinH;       // +16  caller's read handle to dup into child fd 0, or 0xFFFFFFFF
    uint32_t elevate;      // +20  nonzero = spawn with uid 0 (D4; caller must be elevated/root)
    uint32_t pathPtr;      // +24  low 32 bits of the path pointer
    uint32_t pathPtrHi;    // +28  high 32 bits (so the block is 64-bit-clean)
    uint32_t argsPtr;      // +32  low 32 bits of the args pointer
    uint32_t argsPtrHi;    // +36  high 32 bits
} SpawnReq;                // +40  (padded to 40 bytes)
```

Validation (kernel, before any load): `reserved == 0`; `pathLen` in 1..260
and `argsLen` in 0..128; `pathPtr`/`argsPtr` in the user window with their
lengths; `stdoutH`/`stdinH` either 0xFFFFFFFF or an open handle **in the
caller's table** in the right direction (A5); `elevate` requires the caller
to be elevated or root (A4/D4). Any failure returns the matching negative
code (`-1` EFAULT for a bad pointer/length, `-5` EINVAL for a bad flag/handle,
`-3` EACCES for a non-elevated `elevate`). The child gets a **copy** of the
path/args (they live in the child's inherited env/arg block, decision 3), so
the caller may reuse its buffers as soon as 6Ah returns.

Handles (A5): every task owns a per-task table of 8 entries. Slots 0/1/2 are
stdin/stdout/stderr, defaulting to the console; OPEN returns 3–7. SPAWNPATH
redirects the child's 0/1 by passing the caller's already-open handles
(`SpawnReq.stdoutH`/`stdinH`) — the kernel validates the handle exists in the
caller's table and is open in the right direction, never raw inodes. GREP
reads piped stdin via READ(0). Console handles: WRITE works (console output),
READ on a console handle returns -8 (EBADF, D9), not -1.

**A2 redesign note:** `STAT` (60h) is metadata-only and deliberately does
**not** require read permission: directory listing (`DIR`, completion) must
show entries of a readable directory even when a file inside is mode 000.
Content access is gated exclusively by OPEN (CanRead) and the parent-write
rule; path-based metadata queries use the existing 20h RESOLVE, whose result
likewise does not grant content access. ABI.md records this explicitly.

### 3.2 osbkt public API (proposed; the class becomes `public`)

```csharp
namespace OsbKT;
public static unsafe class Syscall {          // today: internal
    // console
    public static void Print(string s);       // 09h
    public static void Print(char* buf, int len);
    public static void PutChar(char c);       // 01h
    public static int  GetChar();             // 6Eh (cooked; -1 none, -2 Ctrl+C)
    public static void Clear();               // 6Fh
    public static void SetColor(int fg, int bg); // 70h
    public static void GetColor(out int fg, out int bg);
    // time
    public static void DateTime(out int y, out int mo, out int d, out int h, out int mi, out int s); // 72h
    public static ulong Ticks();              // 54h
    public static void Sleep(ulong ticks);    // 55h
    // memory
    public static void MemInfo(out ulong totalKB, out ulong usedKB, out ulong freeKB); // 10h
    // environment
    public static int  GetCwd(byte* buf, int cap);                    // 22h
    public static int  GetEnv(char* name, int nameLen, char* buf, int cap); // 63h
    public static bool SetEnv(char* name, int nameLen, char* val, int valLen); // 64h
    // identity (B/C items)
    public static bool Login(char* name, int nlen, char* pass, int plen); // 65h
    public static void Logout();              // 66h
    public static bool Sudo(char* pass, int plen); // 67h
    public static void EndSudo();             // 68h
    public static int  WhoAmI(char* buf, int cap); // 69h
    // files (B items; all permission-checked in-kernel)
    public static int  Chdir(char* path, int len);                    // 24h
    public static bool Mkdir(char* path, int len, int mode = 0755);    // 25h
    public static bool Rmdir(char* path, int len);                    // 26h
    public static bool Unlink(char* path, int len);                   // 27h
    public static bool Rename(char* oldP, int oldL, char* newP, int newL); // 28h
    public static bool Link(char* src, int sL, char* dst, int dL, bool symbolic); // 29h
    public static int  Open(char* path, int len, int flags);          // 2Ah → handle
    public static int  Read(int h, byte* buf, int len);               // 2Bh
    public static int  Write(int h, byte* buf, int len);              // 2Ch
    public static void Close(int h);                                  // 2Dh
    public static int  Seek(int h, int off, int whence);              // 2Eh
    public static bool Chmod(char* path, int len, int mode);          // 2Fh
    public static bool Chown(char* path, int len, int uid);           // 61h
    public static bool Stat(uint ino, byte* out32);                   // 60h (metadata-only, no read check, A2)
    public static int  Readlink(char* path, int len, char* buf, int cap); // 62h
    public static bool Resolve(char* path, int len, out uint ino, out ushort mode, out ushort uid, out ulong size); // 20h (extended stat)
    public static bool DirNext(uint ino, int index, byte* outBuf, int cap); // 21h (now permission-checked)
    // processes (B items)
    public static int  SpawnPath(char* path, int pL, char* args, int aL, int stdoutH, int stdinH); // 6Ah (builds a SpawnReq*, A5)
    public static int  SpawnPathEx(SpawnReq* req);      // 6Ah (pointer form, E1; req fields per 3.1a)
    public static int  SpawnBuiltin(int id);                          // 80h (HELLO/BAD/SPAWNER/DEMO?)
    public static int  Wait(int pid);                                 // 81h
    public static int  GetPid();                                      // 30h
    public static bool Kill(int pid);                                 // 6Ch
    public static bool SetForeground(int pid);                        // 6Dh
    public static int  TaskList(byte* outBuf, int cap);               // 6Bh
    public static int  GetArgs(byte* buf, int cap);                   // 23h
    public static void Exit(int code);                                // 4Ch
    // ABI (A8)
    public static ushort GetABI();                                    // 82h (osbkt startup check)
    // network / PCI (C items)
    public static int  NetInfo(byte* outBuf); public static bool NetSet(uint ip, uint mask, uint gw);
    public static int  NetDhcp(byte* outBuf); public static int Ping1(uint ip, int seq, out int rttMs);
    public static int  PciEnum(byte* outBuf, int cap); public static bool PciDetail(int bus, int slot, int func, byte* outBuf);
    // machine (privileged — root only, decision 5)
    public static void PowerOff(); public static void Reboot();        // 58h/59h (root-gated)
}
```

Plus osbkt-owned ring-3 infrastructure (moved from osb.shell/osb.xwin):
`crt0` (`_start`), the `INT 21h` stub, the user linker script (`user.ld`),
and the OSBX packaging tool (`mkosbx.py`) — per architecture rule 4.

### 3.3 Design choice: pipes / stdout redirection (replaces the VGA-capture hack)

* **Option A — kernel pipe objects.** `SPAWN` gains a stdout target = pipe;
  stage A's 01h/09h writes land in a kernel ring buffer; stage B reads it.
  Pros: true streaming, no temp files. Cons: new kernel object + lifetime
  rules, 4 KiB-class buffer forces blocking writes (needs a "would block"
  protocol or big buffers), deadlock-prone under the 8-slot table; most code.
* **Option B — temp-file redirection (DOS-authentic).** Shell creates
  `%TEMP%\PIPE<n>.TMP` via OPEN (write), spawns stage A with that **open
  write handle** as stdout (SPAWNPATH RDX), waits, LSEEKs the handle back to
  0, then spawns stage B with the same file as an OPEN read handle passed as
  stdin (SPAWNPATH RCX). Pros: tiny kernel surface (handles only — no new
  objects, no inode passing, no flags), bounded by disk not RAM, matches how
  DOS-era shells did pipes. Cons: disk churn; temp cleanup; visible `.TMP`
  files.
* **Option C — capture in the shell.** Impossible from ring 3 (VGA memory is
  U/S=0 and there is no other-process output bus). Refuted; listed to close
  the legacy mechanism out.

**Recommendation: B — APPROVED (decision 1)**, with amendments:
* **A6 — pipe temp files:** created inside a **per-user 0700 temp directory
  `$HOME\TEMP`** (created at login like `$HOME\TRASH`; NOT the world-writable
  0777 `A:\OSB\TEMP`), mode `0600`, unique names (`PIPE0001.TMP`…),
  cleaned up on normal pipeline end **and** on Ctrl+C abort (shell owns
  cleanup; kernel never leaks them). **D8 rationale:** `%TEMP%` is 0777 with
  no sticky semantics — another user could unlink or replace a 0600 pipe
  file between stages (TOCTOU). A 0700 per-user dir closes this without
  kernel changes; sticky-bit UNLINK/RENAME semantics were considered and
  rejected (more kernel surface, DOS-inauthentic). Cross-user pipelines are
  not supported (single-user box, DOS-like).
* **A5 — redirection is handle-based:** SPAWNPATH (6Ah) takes already-open
  write handles (`RDX` = stdout handle or -1 = console, `RCX` = stdin handle
  or -1), validated kernel-side against the **caller's** handle table; raw
  inodes are never accepted. GREP reads via READ(0).
The observable behavior (`A | B` feeds A's output as B's stdin, `GREP` last)
is preserved; `%TEMP%` already exists and is world-writable (0777). The
4096-byte legacy cap becomes a documented limitation of the temp file size
instead of a silent truncation.

**D5 — architecture rule: single output sink.** Built-ins run inside the
shell process, so SPAWNPATH redirection (which only affects a *child*
process) cannot capture their output — `DIR | GREP` and `TYPE f | GREP`
would capture nothing. Rule: **from the first shell slice onward, ALL shell
output goes through one output-sink abstraction** — either the console
(default) or an open handle (temp file for a pipe stage, redirected file).
Built-ins never call `PrintString`/`PutChar` directly; they write to the
current sink (a tiny `Out` module: `Out.Print/Out.PutChar/Out.Newline`,
swappable per command). Consequences: (1) pipes work for built-ins AND
externals uniformly; (2) `>` style redirection later is the same mechanism;
(3) the shell's own prompts/errors always go to the console sink, never to
a redirected one (so a piped command's prompt does not pollute the pipe).
This rule binds slice 1 (line editor) onward — retrofitting it later would
touch every command module.

### 3.4 Design choice: session identity and login

* **Option A — kernel-owned session.** The console task carries `uid` (+name,
  home, elevated flag) in the scheduler slot; LOGIN/SUDO syscalls mutate it
  after kernel-side verification; every file syscall reads it. Shell prompts
  and calls LOGIN. Pros: ring 3 cannot fake a uid; single source of truth.
  Cons: kernel gains a small session table; LOGOUT state = uid "none" (all
  privileged ops denied).
* **Option B — shell-owned users, kernel trusts SETUID.** Shell verifies and
  tells the kernel "I am root". Pros: none. Cons: any ring-3 program can call
  SETUID directly. **Rejected — violates rule 2.**
* **Option C — per-process uid inherited at spawn.** uid stored per task,
  copied from parent at spawn; SUDO spawns a child rather than elevating.
  Pros: no shared mutable session. Cons: built-ins run **in the shell
  process**, so SUDO of a builtin cannot work this way; two uid stores to
  keep in sync.

**Recommendation: A — APPROVED (decisions 3, 8)**, refined by amendments:
* **Session model (A1/1c):** the console session starts **with no uid**
  (`$USER` empty, `$HOME` default). Kernel owns the Users table and the
  session uid; LOGIN/LOGOUT are syscalls; the `login:` prompt is shell
  presentation. `Users.Init()` must run at **boot time** (kernel-side), not
  from the old unreachable `Shell.Run()` (fixes X13).
* **A3 — LOGIN rules:** passwordless LOGIN is allowed **only at the initial
  boot login** (console session has no uid yet). From an authenticated
  session, LOGIN always requires the password. PASSWD plaintext storage is
  **recorded debt** (documented in ABI.md; hash later).
* **A4 — SUDO scoping:** SUDO elevates **only the calling pid**, and the
  elevation reverts automatically when the command ends (background/`&` tasks
  never inherit elevation; SETFG/SPAWN never propagate it). END_SUDO stays
  as the explicit restore for built-ins.
* **D3 — LOGIN/LOGOUT are session-leader-only:** both syscalls are accepted
  **only from the session-leader task** (the task that owns the console
  session — in practice the shell's main task; the kernel tracks its pid).
  Any other task calling LOGIN/LOGOUT gets -3 (EACCES). Rationale: without
  this, a background task that outlives LOGOUT could call `LOGIN("root","")`
  while the console session has no uid — passwordless root login would be
  reachable from a non-console context (the hole this closes).
  **LOGOUT also terminates the departing user's other tasks, keyed by login
(session) id, not by effective uid (E2).** Every task carries a `sessionId`
inherited from its parent at spawn: the session leader's id at boot, copied
unchanged into every SPAWNPATH child (including `SUDO prog.exe` children that
run as uid 0 — they keep the leader's `sessionId` even though their effective
uid is 0). LOGOUT kills **every task whose `sessionId` equals the departing
session's id**, except (a) kernel tasks (`sessionId == 0`, i.e. the console
and kernel-internal tasks) and (b) the session leader itself, which survives
to print the next `login:`. Keying on uid instead would be wrong: a
`SUDO prog.exe` child runs as uid 0, so a uid-based sweep would leave it
alive as an orphan root task after a non-root user logs out. Keying on
`sessionId` catches it (it inherited the departing user's session) while
never touching kernel tasks (session 0) or the leader. Trade-off: this
matches DOS single-session semantics (a user's background work does not
survive logout); the alternative — letting tasks survive and blocking LOGIN
without a console uid — was rejected because orphaned ring-3 tasks with a
stale uid are a privilege-ambiguity source (whose credentials do they run
under after logout?).
* **D4 — SUDO for external programs (elevated spawn):** `SUDO <external>`
  spawns the child **with uid 0 for its lifetime**; the kernel validates the
  SUDO password/sudoers once (as for built-ins), then SPAWNPATHs with an
  elevated-uid flag checked against the caller's **active** elevation
  (caller must currently be elevated or root — an unelevated task cannot
  request an elevated spawn even with a valid password it captured). The
  caller's own elevation **reverts immediately after the spawn** returns
  (SUDO does not stay elevated while the child runs; only the child is
  uid 0). For built-ins, elevation lasts for the builtin's execution (A4).
  This keeps A4's "never propagates to background tasks" while making
  `SUDO some-tool.exe` actually work.

### 3.5 Design choice: where PASSWD/SUDOERS live

* **Option A — kernel owns the files.** Kernel reads `A:\OSB\CONF\PASSWD` /
  `SUDOERS` at mount into kernel tables (the `Users.cs` logic moves into the
  kernel, minus presentation); mutations only via root-checked syscalls
  (USER /ADD → new account syscall or a generic "write PASSWD" root-gated
  call). Shell never sees password material. Pros: credentials never enter
  ring 3; one parser. Cons: kernel must provide account-list query +
  add/change/del/sudo-list syscalls (small).
* **Option B — shell reads PASSWD via file syscalls.** Kernel verifies LOGIN
  by re-reading the file each time. Cons: password hashes (here: plaintext!)
  readable by any ring-3 program; duplicates parsing. **Rejected.**
* **Option C — kernel verifies, shell stores.** Split brain; Cons: two
  writers of the same file.

**Recommendation: A**, with the account-mutation surface kept minimal:
`USERLIST` (query), `USERADD/USERDEL/USERPASS/USERCHHOME`… or a single
`USEROP` with subfunction. Exact shape defined in the users slice's ABI.md
entry. `USER /CHANGE` (own password) is allowed for any logged-in user;
`/ADD`, `/DEL`, `/SUDO`, `/UNSUDO` root-only — enforced kernel-side.

### 3.6 Design choice: spawn-by-path and arguments

* **Option A — path + raw arg string (DOS `COMMAND.COM` style).** 6Ah takes
  path (ptr/len) and args (ptr/len), kernel resolves + exec-checks + loads
  OSBEXE + stores the string (today's `Scheduler.SetArgs`, 96 B). Shell does
  PATH search with RESOLVE probes. Pros: smallest change, args retrieveable
  with existing 23h, no argv memory model. Cons: 96-byte cap (raise to 128
  or 256 in ABI.md); no quoting rules beyond what the shell already does
  (raw string).
* **Option B — argv array.** Shell tokenizes into a kernel-copied array of
  {ptr,len}. Pros: proper quoting/UTF-8 future. Cons: ring-3 must build and
  validate an array in its block (more ABI surface, bounds checks); nothing in
  today's behavior needs it.
* **Option C — ino instead of path.** Shell RESOLVEs, passes ino. Cons: TOCTOU
  vs. exec check, less DOS-like, and PATH search is presentation anyway.

**Recommendation: A — APPROVED (decisions 7, 8), 128-byte cap**, raw-string
semantics identical to `SetArgs` today (trailing spaces trimmed). Redirection
is handle-based: the seven inputs travel in a `SpawnReq` block passed by
pointer (3.1a, E1) whose `stdoutH`/`stdinH` are caller-validated open handles
(A5), not path+flag. TASK/PS shows **all tasks** (decision 4). Args live per
task; SPAWN copies the parent's env and args (decision 3).

### 3.7 Design choice: console input delivery

* **Option A — cooked GETCHAR (kernel line discipline-lite).** Kernel merges
  the two IRQ rings, translates scancodes (the `MapScancode` table is
  hardware knowledge → kernel), **consumes Ctrl+C** (serial 0x03 / PS/2
  Ctrl+C) exactly like the legacy `ConsoleControl` and returns it to ring 3
  only as `-2` — the byte `0x03` is never delivered as data (D2), delivers
  arrows as `ESC [ A/B` for both devices, `-1` when empty. Shell does all
  editing. Pros: presentation stays in the shell (rule 3); one input path
  for keyboard and serial; XWIN's 52h raw scancode call stays untouched for
  the desktop. Cons: the kernel gains a small translation table (hardware
  concern — correct home).
* **Option B — raw scancodes + serial merged, shell translates.** Cons:
  duplicates the scancode map in ring 3 (hardware table in presentation
  code) and forces the shell to distinguish devices.
* **Option C — keep 52h, add a serial byte channel.** Cons: the shell still
  can't tell arrows from ESC+chars without its own state machine per device;
  two code paths; Ctrl+C needs a third channel. (This is exactly today's
  broken state.)

**Recommendation: A — APPROVED (1b).** GETCHAR returns: char value 0–255,
`-1` = empty, `-2` = Ctrl+C (A7: GETCHAR-specific return space, not an
error). **Resolved (D2, supersedes earlier drafts): the kernel consumes the
Ctrl+C event — serial 0x03 and PS/2 Ctrl+C never reach ring 3 as the byte
0x03; `-2` is the only representation. The shell reacts to -2 by discarding
the current line and printing a fresh prompt (legacy parity).**
Observable behavior (both consoles edit identically, Ctrl+C aborts, arrows
recall) is what the legacy in-kernel editor already does internally; the
kernel simply exposes the *characters*, not the *editing*.

**P0.4 (phase 1c prelude) — PS/2 letter case in ConsoleInput.** The legacy
`Keyboard.MapScancode` (and the legacy `Shell.ReadLine`/password path that
calls it) returns **UPPERCASE for every letter** regardless of Shift, and has
**no CapsLock handling at all** (scancode 0x3A is ignored; Shift only affects
digits/punctuation). So legacy passwords and typed letters are effectively
always uppercase. The ring-3 `ConsoleInput` now **departs from that**:
`PushScancode` tracks CapsLock (0x3A toggles, 0xBA ignored) and emits a letter
**lowercase unless Shift XOR CapsLock**. The legacy `Keyboard` path is left
**untouched** (the in-kernel shell, if ever revived, keeps its historic
behavior). Rationale: a ring-3 line editor that cannot type lowercase is not
usable for paths/commands; the departure is confined to `ConsoleInput` and is
golden-pinned by 25-getchar (PS/2 `a` now echoes `a`).

**P0.5 (phase 1c prelude) — Flush timing.** `ConsoleInput.Flush()` (called on
every spawn today) clears the cooked ring and the Ctrl+C marker but **no longer
resets `_shift`/`_ctrl`**: a spawn must not forget that a physical modifier is
held. The flush-on-spawn itself is a temporary measure; it **moves to the
foreground hand-off in slice 6**, where a background spawn must not eat the
shell's type-ahead (see §5 slice 6).

### 3.8 Design choice: HALT, POWEROFF, REBOOT privilege

* **Option A — root-gated syscalls; drop HALT from the shell.** `halt`
  cannot execute in ring 3 (privileged instruction → #GP → task killed).
  POWEROFF/REBOOT become uid-0-only syscalls. Cons: none; HALT disappears
  (documented deviation).
* **Option B — HALT as "kill all + poweroff" alias.** Confusing vs. legacy
  message.
* **Option C — leave HALT as a kernel demo.** It already is not a shell
  command in the userspace world.

**Recommendation: A — APPROVED (decision 5, 6, 7)**: POWEROFF/REBOOT
root-only (X8, deviation from legacy open access, listed for approval —
approved); HALT **dropped** (documented deviation); args cap raised to
**128 bytes**.

### 3.9 Design choice: file I/O shape (handles vs whole-file)

* **Option A — handle API (2Ah–2Eh + 60h).** OPEN/READ/WRITE/CLOSE/LSEEK
  with a per-task handle table. Pros: bounded 4 KiB buffers, COPY of large
  images without a 256 KiB shell buffer, LSEEK enables `.ORIG`/COLOR.CFG
  rewrites. Cons: handle table in kernel (~40 lines).
* **Option B — whole-file calls** (`READFILE(path,buf,cap)`, `WRITEFILE`).
  Cons: 256 KiB-class buffers in ring 3 (image area is 512 KiB — too tight
  with stack), truncation bugs, two extra syscalls anyway for append.

**Recommendation: A** — also matches the names osbkt's README already
advertises (`OpenFile`, `ReadFile`, `WriteFile`).

### 3.10 Design choice: environment ownership

* **Option A — per-task kernel env + 63h/64h (APPROVED, decision 3).**
  Env lives kernel-side but is stored **per task and copied at spawn** (a
  child's `SET` never alters the parent's env; DOS-authentic snapshot
  semantics). `HOME`/`USER` are kernel-locked once the session uid exists.
  **Recommendation: A.**
* **Option B — shell-local env, passed at spawn.** Cons: env inheritance
  becomes the shell's job; grandchildren lose it; DOS semantics broken.
* **Option C — kernel env, shell caches.** Two copies drift.

---

## 4. (d) Verification of suspected blockers (evidence)

> **Snapshot status (D1):** this section records the state **before Phase 1a**
> (analysis time). X1/X3/X4/X5/X6/X14 and parts of X15 are **resolved** by
> the Phase 1a commits (osb.shell 96092f7, osbkt a2248c6, osb.kernel
> 45ccca5): all three repos build, `./build.sh` produces the ISO, osbkt is
> public with `docs/ABI.md`, sources are single-copy linked, and the shell
> prints its banner on serial. The remaining blockers (X7–X13, X15 input,
> X16 args cap) are addressed by the slices below.

Verdicts from reading the code and building in this environment
(`dotnet 10.0.400`; `dotnet build` run in each repo).

**X1 — osbkt README/AGENTS advertise file+GetChar APIs that do not exist.**
**CONFIRMED.** `osbkt/README.md:12-16` and `osbkt/AGENTS.md:30` advertise
`CreateFile, OpenFile, ReadFile, WriteFile, DeleteFile, CreateDir, RemoveDir,
Rename, GetChar`. `osbkt/Syscall.cs` contains only: PrintString, PutChar,
GetKeyScancode, GetPid, Exit, Spawn, Wait, MemInfo, GetCwd, GetArgs,
ResolvePath, DirNext. `osb.kernel/Cpu/Syscalls.cs` handles 01h, 09h, 10h,
20h, 21h, 22h, 23h, 30h, 4Ch, 80h, 81h; `SyscallGfx.cs` handles 40h–59h
(graphics/input). No create/read/write/delete/mkdir/rmdir/rename/getchar
syscall exists. The README describes an API that was never implemented.

**X2 — GetKeyScancode (AH=52h) returns raw PS/2 set-1 scancodes; shell
misuses it.**
**CONFIRMED, worse than stated.** `SyscallGfx.cs:125-127` (case 0x52) returns
`Keyboard.Pop()` — raw set-1 make codes, `-1` when empty, fed only by IRQ1
(`Interrupts.cs:111-117`); serial bytes go to a separate ring drained only by
the in-kernel editor. `osb.shell/Program.cs:64-96` treats the value as ASCII
(`scancode >= 0x20 && scancode <= 0x7E`) and does not handle `-1` (busy
loop). Even on a real keyboard this is garbage: the `A` key produces 0x1E
(< 0x20, dropped); only scancodes that numerically fall in the ASCII
printable range pass. Cooked input is required (3.7).

**X3 — SPAWN takes a built-in id; SpawnFile not exposed.**
**CONFIRMED.** `Syscalls.cs:56-63` (case 0x80): `prog = rax & 0xFF` →
`Scheduler.SpawnUser(prog,…)`; `SpawnUser` only knows Layout.ProgBad /
ProgSpawner / hello (`Scheduler.cs:248-269`). `Scheduler.SpawnFile`
(`Scheduler.cs:292`) is `public` **inside an `internal` class** — invisible to
ring 3; its only callers are `Kernel.cs:69` (boot) and `JobControl.cs:54`
(in-kernel shell). The shell cannot start `.EXE` files by path.

**X4 — osb.shell allocates; MiniCoreLib has no allocator.**
**CONFIRMED, and the project does not even compile.** `Program.cs` uses
`new char[255]` (61), `new string(buffer,0,pos)` (84), `Substring` (117/155/
194/224), `c.ToString()` (93), `string.IsNullOrEmpty`, `char.IsWhiteSpace`.
MiniCoreLib's `String` has only `Length` (no ctor/indexer), `Array.Ctor`
"fail-fast" throws (`MiniCoreLib.cs:83`), there is no allocator and the
boxing/cast helpers throw. `dotnet build` in osb.shell: **79 errors**
(e.g. CS1729 string 3-arg ctor, CS0021 string indexer) — the checked-in
shell has never built against its own MiniCoreLib.

**X5 — byte-identical copies; osbkt internal.**
**CONFIRMED.** `diff osb.shell/Syscall.cs osbkt/Syscall.cs` → identical;
`diff osb.shell/MiniCoreLib.cs osbkt/MiniCoreLib.cs` → identical; md5 of the
MiniCoreLib in kernel/osbkt/osb.shell/osb.core/osb.xwin: all
`5b69da78…` (five copies). `osb.shell/Osb.Shell.Userspace.csproj:24-28`
compiles local copies — **no ProjectReference to osbkt** (the AGENTS.md
"Depends on osbkt/osb.core" is aspirational). `osbkt/Syscall.cs:6`:
`internal static unsafe class Syscall` — even if referenced, nothing is
public.

**X6 — kernel build.sh references the old monorepo layout.**
**CONFIRMED.** `build.sh:455` `XWIN_ROOT="$(cd "$ROOT/../Osb.Xwin" …)"`,
`:620-621` `../Osb.Shell.Userspace/Osb.Shell.Userspace.csproj`, `:633`
`../Osb.Shell.Userspace/userspace.S`, `:636` `../Osb.Xwin/user.ld`, `:641`
`../Osb.Xwin/mkosbx.py`. Neither `Osb.Shell.Userspace` nor `Osb.Xwin` exists
in the split layout (repos are `osb.shell` / `osb.xwin`). With
`set -euo pipefail`, the failing `cd` in `$()` makes the assignment at line
455 fail → the build **aborts there** (verified: the same construct exits 1).
Even past that, line 620 would fail. Additionally `build.sh` duplicates the
ISO build block (lines 648–664 appear twice).

**Additional blockers found (not in the suspected list):**

* **X7 — no permission enforcement in any syscall.** `Users.CanRead/
  CanWrite` are called only from `Shell/Commands/*` (FileCommands, Trash*,
  UserCommands — 25 call sites). `20h`/`21h` (resolve/dirnext) do a range
  check and go straight to ext4: any ring-3 program can resolve + read **any**
  inode (PASSWD included) and enumerate any directory. GREP has no check even
  in the legacy shell (`GrepCommand.cs:69-76` — only IsReg).
* **X8 — privileged syscalls callable by anyone.** `58h` POWEROFF and `59h`
  REBOOT (`SyscallGfx.cs:158-166`) have no uid check: any ring-3 program
  (including a crashed/trojaned SHELL.EXE) can power off or reboot the box.
* **X9 — syscall pointer validation is start-only → ring-0 panic from ring 3.**
  `01h` (rdi checked, len up to 4096 → rdi+len can cross UserLimit), `09h`,
  `20h` (path rdi/rsi unbounded), `22h` (writes up to 256 B at rdi+256),
  `23h`, `10h`… A single bad pointer page-faults at CPL0 → `Panic()`
  (`Interrupts.cs:183-195`) halts the whole system. Enforcement/robustness
  blocker for every new syscall (ABI.md must mandate `ptr+len ≤ UserLimit`).
* **X10 — no environment syscalls.** `Environment` is kernel-side session
  state with `Init()` at boot (`Kernel.cs:63`) but no ring-3 accessor: a
  userspace shell cannot read `$PATH`/`$PROMPT` or implement `SET`. (23h
  gives args only.)
* **X11 — no CHDIR syscall.** `Cwd` is kernel-side; `22h` only reads it. CD
  in a ring-3 shell is impossible today.
* **X12 — ring 3 cannot write the console directly.** Kernel identity map is
  U/S=0 → `0xB8000` is invisible to ring 3; every byte must go through
  01h/09h. `CLS` (VGA clear) and `COLOR` (attribute) need console syscalls;
  serial output already mirrors automatically.
* **X13 — the live boot path has no login/users/color/trash setup.** With
  `Shell.Run()` unreachable, `Users.Init()` (PASSWD/SUDOERS seeding,
  `EnsureAll`, TEMP chmod) and `LoadColor()` never run on the current boot
  path. Whatever replaces the legacy shell must re-create these semantics
  (kernel-side for Users, shell-side for COLOR load).
* **X14 — no ABI documentation anywhere.** `osbkt/docs/` does not exist; the
  kernel's only ABI record is comments in `Syscalls.cs`/`SyscallGfx.cs` and
  the (partly stale) README. Rule 5 requires `osbkt/docs/ABI.md` first.
* **X15 — serial input never reaches ring 3.** `Serial.Pop()` is called only
  by `Shell.Session` (in-kernel). The userspace shell is headless-deaf: the
  standard `./build.sh run` (`-serial stdio`) cannot drive it at all.
* **X16 — argument cap 96 B** (`Scheduler.ArgsCap`, `Scheduler.cs:42`;
  `TryExternal` truncates at 92, `Shell.cs:668`). Workable, but long PATHs +
  args will silently truncate; raise in the ABI.
* **X17 — stale comment in `osb.xwin/user.ld`** (says image linked at
  0x400000; actual `. = 0x2000000` matching `Layout.UserBase`). The file
  moves to osbkt in Phase 1; fix the comment then.

Build status **at snapshot time (pre-1a, D1)**: `osb.kernel` → **0 errors**
(35 warnings); `osbkt` → **0 errors**; `osb.shell` → **79 errors** (X4); full
ISO build (`./build.sh`) → **fails at line 455** (X6). (Current status: all
green after Phase 1a — see the header.) QEMU is present
(`qemu-system-x86_64`), so slice validation can run here.

---

## 5. (e) Migration order (vertical slices) — A1 amended

Each slice: **ABI.md entry → kernel syscall (with enforcement) → osbkt
wrapper → shell feature → docs → QEMU acceptance run** (golden transcript
per A11), stop, summarize per repo, wait for approval.

**Phase 1a — Build/tooling only (this project).**
osb.shell references osbkt (public API) + osb.core (MiniCoreLib, one copy —
linked sources, sibling-checkout with clear failure, A10); `crt0`
(userspace.S), the INT 21h stub, `user.ld`, `mkosbx.py` move into osbkt;
fix `osb.kernel/build.sh` for split repos (`../osb.shell`, `../osbkt`,
`../osb.core`, drop duplicate ISO block); `docs/ABI.md` v0 documenting every
EXISTING syscall as implemented (A7 table incl. error-code semantics and
GETCHAR's return space); shell compiles zero-alloc (banner + halt loop is
enough); README/AGENTS in all three repos stop advertising non-existent
APIs; QEMU serial smoke harness + A11 golden-transcript capture.
*Acceptance:* three repos build; `./build.sh` produces the ISO; QEMU serial
shows the userspace banner; smoke tests run unattended.

**Phase 1b — Kernel hardening (X9, X15).**
GETCHAR (6Eh, 3.7-A, A7 return space) + serial input into ring 3 (X15);
SPAWNPATH (6Ah) **upgraded from the 1a.1 minimal form (RDI=path, RSI=len, RDX=args,
RCX=argsLen) to the A5 handle-based `SpawnReq` form** (3.1a), 128-byte args;
**full pointer validation (ptr+len ≤ UserLimit) on every syscall** (X9, kills the
ring-3 → ring-0 panic vector).
*Acceptance:* typing works over serial; bad pointers return -1 instead of
panicking; test program started by path with args. (The 1a.1 minimal SPAWNPATH
already passes `21-exit7`: TWait.EXE SPAWNPATHs TExit.EXE and WAIT returns 7.)

**Phase 1c — Identity foundation (do before any non-root slice).**
Kernel-owned Users table (PASSWD/SUDOERS parsed at boot, `Users.Init()` at
**boot time** — X13), console session uid, LOGIN/LOGOUT/WHOAMI (65h/66h/
69h, A3 rules), shell `login:` prompt; POWEROFF/REBOOT root-gated (58h/59h).
*Acceptance:* boot → `login:`; root passwordless login; wrong password
denied; logged-out session cannot do privileged ops.

> **1c golden re-capture (04-whoami, 12-login, 25-getchar, 27-ctrlc-order).**
> These goldens were captured from the legacy in-kernel shell (5078f4b), but
> two pre-1c changes moved the line reader away from byte-exact legacy parity:
> (a) **1b.1 P0.5-adjacent CRLF swallow** — the ring-3 reader now swallows the
> LF that follows a CR (a serial CRLF is one Enter), so the legacy's "leftover
> `\n` read as an empty command → extra prompt" no longer happens; (b) **1c
> login** — the shell now boots to `login:` and uses a `user@OSB cwd>` prompt
> instead of the fixed `A:\OSB>` + `ECHO` of 1b. The four cases were
> re-captured from the 1c shell: 04-whoami/12-login keep their legacy
> *semantics* (passwordless root login, WHOAMI=root, LOGOUT→login) but their
> transcripts reflect the fixed reader + login prompt; 25-getchar/27-ctrlc-order
> gained an LF-only login prelude (`sendlf root`) so they still exercise the
> GETCHAR line editor (echo, backspace, in-band Ctrl+C, CRLF) via the 1c shell.
> The dedicated 1c security goldens (28-identity, 29-logout-kills, 30-sudo-scope)
> are new `spec` cases booting TId/TLogout/TSudo.EXE.

> **1c.1 prelude (Q1–Q8) — tighten the 1c security before Slice 2.**
> Landed on `phase-2` as its own commit (osb.kernel + osb.shell; the ABI stays
> v4, so osbkt is unchanged). Summary of the eight items:
> - **Q1** `TId`'s child now asserts a non-leader child's LOGIN(root)/LOGOUT are
>   `-3` and WHOAMI stays `tester` (folded into the existing childuid check).
> - **Q2** `TLogout` asserts that after LOGOUT every privileged call is refused
>   (WHOAMI/SUDO/POWEROFF/REBOOT/spawn ROOTONLY == `-3`) and a fresh LOGIN works.
> - **Q3** `LOGIN` rejects an empty or over-long name (`nameLen <= 0` or
>   `>= Users.NameCap`) outright — it never truncates. A 15-char user
>   (`abcdefghijklmno`) is seeded in WITH_TESTS; `<name>QQ` (17 chars) is
>   rejected. `TId` is now n=10.
> - **Q4** the boot program is created with session 1 / uid -1 **before** it
>   becomes READY (`SpawnFileInode(boot:true)`), so it can never run a tick with
>   the Console's uid 0. `SeedSlot` now takes explicit
>   `(sessionId, realUid, uid, gid)` instead of inheriting `_current`.
> - **Q5** a LOGIN while a user is logged in first performs the LOGOUT teardown
>   (kill session tasks except leader+kernel), THEN authenticates. This deviates
>   from legacy `UserCommands.Login` (which just re-ran SessionSetup and left the
>   old session's tasks alive) — recorded here and in
>   `tests/golden/DEVIATIONS.md`. `TeardownSession` is shared by LOGOUT and
>   re-LOGIN so the two paths cannot drift.
> - **Q6** legacy transcripts for 04/12/25/27 are preserved in
>   `tests/golden/legacy/` with per-case deltas in
>   `tests/golden/DEVIATIONS.md` (banner removal, login-first, the `echo:` debug
>   line removed from the line reader, and the PS/2 lowercase departure).
> - **Q7** mutation suite `tests/mutations/` + `tests/mutations.sh`: 11 patches,
>   each breaks exactly one check and is caught (11/11; see `docs/MUTATIONS.md`).
> - **Q8** this note plus the validation below.
>
> **PASSWD/SUDOERS (path, format, legacy storage).** Kernel-owned at
> `A:\OSB\CONF\PASSWD` (`name:password:uid:home`, plaintext, `PassCap` 32) and
> `A:\OSB\CONF\SUDOERS` (one user per line); both are mode 0600 root:root (seeded
> by `build.sh`). Legacy stored the same plaintext table in-kernel (`Users.cs`,
> recorded debt in §3.5). No syscall ever returns password material — WHOAMI
> returns the user **name** only. **Accepted risk (legacy-compatible):** root is
> passwordless and SUDO does not prompt for a password; both match the legacy
> shell and are left as-is for this prelude (hashing + a SUDO password are future
> slices).
>
> **P0.1 grep (20h/21h/6Ah).** `0x20` RESOLVE (`Cpu/Syscalls.cs:193`) and `0x21`
> DIRNEXT (`:228`) are intentionally open — DIR must list unreadable files (A2),
> and the I6 gate says *not* to add a check to RESOLVE. `0x6A` SPAWNPATH (`:73`)
> resolves, type-checks, then calls `SpawnExecutable(mode,uid,gid)` and refuses
> with `-3` EACCES **before any task is created** (B3); 1c wires the caller's real
> uid into that check. No 6Ah caller bypasses the exec check.
>
> **Validation.** Golden at the current level (1c): **13 pass / 0 fail / 18 skip**,
> three runs (156 s each). Mutations: **11/11 caught**. Legacy parity: the 20
> `kind=legacy` cases were captured from the in-kernel shell @ 5078f4b; a fresh
> 20/20 legacy re-run needs that kernel rebuilt with its old sibling layout and
> shims (see `capture.sh`), which is out of scope for this prelude — the four
> recaptured cases' legacy transcripts are preserved in `legacy/` and their deltas
> are in `DEVIATIONS.md`.

Then, one slice at a time:

**Slice 2 — line editor / history / completion.**
Shell-side editor over GETCHAR: 128-char line, Backspace, Up/Down + draft,
`(:N)`, `HISTORY [n]`, TAB completion (commands/flags/files via
permission-checked 21h + 60h).
*Acceptance:* recall `(:1)`; `HISTORY` numbers; Up/Down across serial arrow
sequences; `<TAB>` completes `DI`→`DIR`, `TY`→`TYPE`, `A:\OS<TAB>`.

> **2.1 prelude (A1–A8) — line-editor redraw / completion before Slice 3.**
> Landed on `phase-3` as its own commit (osb.kernel + osb.shell + osbkt). Items:
> - **A1/A2** `Vga.PutChar` mirrors the logical cursor into the CRTC, and the
>   line editor's Redraw returns to the line start with wrap-aware `\b` x
>   `screenCur` (the old `\r` redraw only worked on one row). New `vga`
>   directive in `drive.py` captures the 80x25 text buffer + the CRTC cursor
>   (`vga.py`), with goldens 31-edit-wrap / 32-edit-history / 33-complete-wrap /
>   35-edit-scroll; each was verified to fail against the old redraw.
> - **A3/A4/A5** completion replaces the token with the canonical-case name
>   (so `t<TAB>` -> `TRASH`, not `tRASH`), echoes only the new chars when the
>   typed prefix is already canonical (§1.4 "extended in place"), lists
>   ambiguous matches two spaces apart and extends to the LCP; root-anchored
>   (`\OSB\...`) and absolute (`A:\...`) tokens keep their directory prefix.
>   11-completion was extended (empty token, `H` listing, absolute/root/subdir
>   paths, no-match, over-long token, lowercase/mixed `ver`/`vEr` and a
>   line-cap token) and recaptured as a spec case; 33 adds a mixed-case path.
> - **A6** osbkt gains `Syscall.ResolvePath(char*, …)`; the register-level
>   `Raw()` moved to `Syscall.Raw.cs` (test programs only), so the shell no
>   longer calls `Raw`.
> - **A7** a lone ESC is discarded after a 5-tick wait; new 34-escape.
> - **A8** default (non-WITH_TESTS) smoke passes H-B; KVM keystroke→echo
>   latency: median 1.2 ms, p95 9.3 ms, max 10.4 ms (one 100 Hz tick).
>
> **Validation.** Golden at level 2: **19 pass / 0 fail / 17 skip** (three runs,
> ~195 s each; 31/32/33 carry VGA goldens). Mutations: **11/11 caught**.

**Slice 3 — prompt / environment / expansion.**
`$PROMPT` tokens, `$VAR` expansion in the line, `SET` over 63h/64h
(per-task env, copied at spawn — decision 3); `$USER`/`$HOME` set at login
slice ordering: 1c already created the session, so `$USER` reflects the
login state.
*Acceptance:* `SET FOO=bar`, `ECHO $FOO`, `SET PROMPT=$P$G`, prompt shows cwd.

**Slice 3 — STATUS: DONE.** Kernel per-task env (63h/64h, ABI v5), copy-at-spawn,
USER/HOME locked, LOGOUT/LOGIN isolation, shell PROMPT rendering, SET, and
external command dispatch over PATH (absolute entries only; relative entries
ignored). Golden: 03-set, 36-env, 37-envapi. Mutations: M12+ (USER read-only,
copy isolation, relative PATH ignored).

**Slice 4 — filesystem read commands.**
DIR (long + /W), CD (no permission check, parity — decision 2), PWD, TYPE,
SIZE; kernel enforcement: read-checks in 20h/21h/2Bh (GREP included —
decision 9); 24h CHDIR; 60h STAT (metadata-only, A2); 62h READLINK.
*Acceptance:* `DIR`, `DIR /W`, `CD A:\OSB`, `TYPE A:\OSB\README.TXT`; as
non-root with `CHMOD 600` file: `Permission denied` (kernel-enforced); DIR
still lists the unreadable file (A2).

**Slice 4 — STATUS: DONE.** Kernel: 24h CHDIR, 2Ah OPEN / 2Bh READ / 2Ch
CLOSE, 60h STAT. I6 read-permission enforcement in OPEN. Shell: DIR, CD, PWD,
TYPE, SIZE. Golden: 05-dir, 06-cd (recaptured).
> **I6 — HARD GATE before slice 4 ships.** Slice 4 adds the first read
> commands (TYPE/SIZE/GREP), so the permission story must be closed *first*:
> 1. **READ/OPEN must enforce the file read permission.** The read path
>    (2Ah–2Eh OPEN/READ, 2Bh GREP input) must call the `Users.CanRead(owner,
>    mode)` check and return `-3` (EACCES) for a non-readable file — a read
>    syscall that ignores mode bits is the hole slice 4 would otherwise open.
> 2. **PASSWD/SUDOERS must be root:root, mode 0600, before any read command
>    exists.** `build.sh` already seeds `sif … mode 0100600` (uid 0), but the
>    gate makes it a release blocker: if the read syscalls ship before the
>    credential files are locked down, a non-root `TYPE A:\OSB\CONF\PASSWD`
>    would dump every password (1c stores them plaintext — see §3.5 debt).
> 3. **20h RESOLVE has no permission check (X7).** RESOLVE only resolves a
>    path to an inode and is intentionally open (DIR needs it to list
>    unreadable files, A2); the read *data* syscalls (READ/OPEN) are where
>    enforcement lives. Do not "fix" X7 by gating RESOLVE — that would break
>    DIR of unreadable files and the `ls`-style listing A2 requires.

**Slice 5 — filesystem write commands.**
MD/RD, DEL→trash + TRASH (list//F//U) as shell logic over 25h/26h/27h/28h/
2Ah–2Eh, REN/MOVE, COPY/CP (shell loop over handles), LN/LNK.
*Acceptance:* create tree, DEL to trash, `TRASH` lists it, `TRASH /U`
restores to the original folder; `REN`, `MOVE` across dirs, `COPY` + `SIZE`
compare; non-root cannot write into root-owned dir (kernel EACCES).

**Slice 6 — processes / jobs.**
SPAWN/RING3 built-ins (80h), DEMO, TASK/PS (all tasks — decision 4), KILL,
WAIT, TAB(pid), `&`, foreground messages, Ctrl+C abort — over 6Bh/6Ch/6Dh/
80h/81h with same-uid/root rules. **D6: 81h becomes interruptible here —
Ctrl+C during WAIT returns -7 (EINTR) to the shell, the child keeps running;
the shell prints `Foreground job aborted (Ctrl+C).`** (ABI.md entry for the
81h change is written with this slice.)
*Acceptance:* `SPAWN &`, `TASK` shows it, `TAB <pid>`, Ctrl+C abort message,
`KILL`, `WAIT` exit code; non-root cannot kill root's task.

**Slice 7 — users / login / sudo / trash config.**
LOGIN/LOGOUT (done in 1c), USER (list/add/change/del/sudo/undsudo), CHOWN/
CHMOD, SUDO (A4: calling pid only, reverts at command end), WHOAMI (done in
1c), COLOR.CFG load at login; kernel owns PASSWD/SUDOERS/session uid (65h–
69h, 61h, 2Fh + USEROP family).
*Acceptance:* `USER /ADD bob secret`; logout; login bob (wrong/right);
bob: `Permission denied` on root file, `Operation not permitted` on CHMOD
of others; `SUDO CHMOD …` after password, reverted after the command;
background task not elevated; `TRASH /U` across users.

**Slice 8 — pipes / GREP.**
`A | B` via stdout-to-temp-file (A6: 0600, unique, owner-only, cleaned on
normal end and Ctrl+C), GREP over handle input (READ(0)).
*Acceptance:* `DIR | GREP OSB`, `TYPE big.txt | GREP /I pattern`, last stage
prints, pipeline in foreground, no `.TMP` left behind after run or Ctrl+C.

**Slice 9 — system & time commands.**
VER/ABOUT/CLS/MEM/TIME/DATE/TICKS/SLEEP/ECHO/HELP pages/HISTORY(already)/
WHOAMI(done); console 6Fh/70h/71h, DATETIME 72h; POWEROFF/REBOOT root-gated
(done in 1c).
*Acceptance:* TIME/DATE/TICKS values, CLS clears over serial, POWEROFF exits
QEMU, non-root POWEROFF denied.

**Slice 10 — network / PCI / PING.**
NET/IFCONFIG (74h–76h), PING loop in shell over 77h, PCI 78h/79h.
*Acceptance:* `NET` shows adapter, `NET AUTO` lease, `PING 10.0.2.2` replies,
`PCI net` details; non-root `NET <ip>` denied.

**Slice 11 — console / color.**
COLOR fg bg, persistence to CONF\COLOR.CFG, reload at login (shell-side),
GET/SETCOLOR syscalls.
*Acceptance:* `COLOR 0A` clears with new attribute; reboot → color restored
(root-persisted); non-root gets "not saved" message.

Legacy code is deleted per slice **only after** approval of that slice's
parity report (decision 10). Every intentional deviation (POWEROFF
root-gating, dropped HALT, pipes as temp files, plaintext PASSWD debt,
FSTAT→STAT metadata-only) is listed in the slice summary.

---

## 6. (f) Do NOT migrate / out of scope (recommendations)

| Item | Recommendation |
|---|---|
| OSLANG interpreter (`OslangKernel.cs` + ~8 partials, ~6.7 kLOC, inside the kernel) | **Stays in the kernel for this project (A12).** It is kernel code, not shell logic; no OSLANG migration happens here. Consequence: the `OSLANG` shell command and `.osl` scripts are **temporarily lost** while the legacy shell is retired — documented deviation. A later project moves OSLANG to an external `OSLANG.EXE` (osb.oslang exists) and re-adds external dispatch. |
| KISS command | **Drop as built-in.** It is a launcher: `OSLANG A:\OSB\BIN\KISS.OSL [file]`; users run `OSLANG …` or a `KISS.EXE` wrapper later. |
| GFX desktop (`Shell/Desktop*.cs`, ~580 lines, currently unreachable from dispatch) | **Do not migrate.** Superseded by osb.xwin (`XWIN.EXE`, ring 3, uses 40h–59h). Delete from the kernel after XWIN parity is confirmed; XWIN remains a separate userspace program. |
| DEMO task | **Keep kernel-side** (scheduler test fixture). Optionally expose as a built-in SPAWN id for `TASK`/`KILL` testing; no shell feature. |
| SPAWN/RING3 built-ins HELLO/BAD/SPAWNER | **Keep kernel-side** as test programs (user.S blobs). Shell keeps `SPAWN [BAD\|SPAWNER]` over 80h for diagnostics, or drops them — decide at slice 5. |
| Legacy `Users.cs` in Shell/ | **Moves into the kernel** (session table + PASSWD/SUDOERS + checks), not into the shell. |
| Vga-capture pipe hack | **Replaced** (3.3) — deleted with the legacy shell. |
| `Environment`/`Cwd` in-kernel singletons | **Stay kernel-side** (session state), exposed via 63h/64h/22h/24h. |
| osb.xwin's private copies (xwin.S, user.ld, mkosbx.py, Sys.cs, MiniCoreLib) | Later cleanup: reuse osbkt's crt0/stub/ld/packaging; XWIN is out of this migration's scope. |

### 6.1 XWIN staging in `osb.kernel/build.sh` (review item 8)

`build.sh` **does** stage `XWIN.EXE`: after building `osb.img` it runs
`../osb.xwin/build.sh` with `STAGE_BIN` pointed at a temp dir and, if that
produces `XWIN.EXE`, `debugfs write`s it to `/OSB/BIN/XWIN.EXE` with mode
0100755 (then `e2fsck`). This is **best-effort**: a missing sibling or a
failing XWIN build prints `WARN: XWIN build failed ... continuing` and does
not abort the kernel build. So the default image ships `XWIN.EXE` only when
the `osb.xwin` sibling is present and builds cleanly; otherwise the image
still builds without it (known gap: the desktop is absent from the image, and
`Shell/Desktop*.cs` was never migrated — §6). XWIN itself is out of this
migration's scope.

### 6.2 Legacy TAB-completion bug — issue draft (review item 9)

The note is in §1.4. Draft issue text to file against the legacy tree
(do **not** fix it in the kernel; the new shell implements completion
correctly):

```
Title: Legacy TAB completion triples-faults on any non-empty token
Labels: bug, legacy-shell, crash

Completion.CmdCount (Completion.cs:28) is 56, but InitCommands
(Completion.cs:336-355) registers 63 names into the fixed CmdTable
(Completion.cs:30-35). The 7-entry overflow corrupts lens[]
(Completion.cs:113-120); NameMatches then reads out of bounds and the box
triple faults on ANY non-empty token (empty-line Tab is safe because
CompleteCommand returns early at `if (tl == 0) return;`).

Repro: boot the legacy image, type `AB` then Tab (serial 0x09 or PS/2
0x0F -> Shell.cs:287-290 -> Completion.Complete). Result: triple fault.
Expected: `AB<TAB>` completes to `ABOUT`.

Fix (for the record, not applied here): set CmdCount to 64 (or derive it
from the InitCommands table size). Verified: bumping CmdCount to 64 makes
`AB<TAB>` complete to `ABOUT`.

Impact on migration: completion cannot be golden-captured from the legacy
build (case 11-completion = NO GOLDEN YET); the userspace shell implements
completion per §1.4 / Slice 2.
```

---

## 7. Decisions record and remaining open questions

Approved (Phase 0 review, "approval with amendments"):

1. **Temp-file pipes** (3.3-B), kernel pipe objects later. With A5 (handle-based
   redirection) + A6 (0600, unique, owner-only, cleaned on end and Ctrl+C).
2. **CD unchecked** (legacy parity): 24h does no permission check; consumers
   re-check on access.
3. **Environment: kernel-side, per task, copied at spawn** (3.10-A): a
   child's SET never alters the parent.
4. **TASK/PS shows all tasks** (legacy behavior).
5. **POWEROFF/REBOOT root-only** (deviation from legacy open access — rule 2).
6. **HALT dropped** from the userspace shell (documented deviation).
7. **Args cap 96 → 128 bytes** (3.6).
8. **One console session; uid copied per task** at spawn.
9. **GREP enforces CanRead kernel-side** (deviation: some files previously
   readable via the legacy gap become denied for non-root).
10. **Legacy code deleted only after per-slice parity approval.**

Amendments applied to this document:

* **A1** — new phase/slice order: 1a (build/tooling) → 1b (pointer
  hardening, GETCHAR, SPAWNPATH) → 1c (kernel-owned Users, boot-time
  `Users.Init`, session uid, LOGIN/LOGOUT/WHOAMI, shell login prompt) →
  then slices 2–11; no non-root acceptance before 1c.
* **A2** — 60h STAT is metadata-only, no read-permission check (DIR lists
  entries of readable dirs even when files inside are unreadable);
  FSTAT-by-inode redesigned as STAT with this documented rule.
* **A3** — LOGIN passwordless only at the initial boot login; from an
  authenticated session LOGIN always requires a password; plaintext PASSWD
  is recorded debt (ABI.md).
* **A4** — SUDO elevates only the calling pid; elevation reverts when the
  command ends; background tasks never inherit elevation.
* **A5** — SPAWNPATH redirection takes caller-validated open handles
  (RDX/RCX), never raw inodes; stdin/stdout handles default to console.
* **A6** — pipe temp files: mode 0600, unique names, owner-only, cleaned up
  on normal end and Ctrl+C.
* **A7** — keep -1 EFAULT / -2 ENOSYS as-is; full table in ABI.md v0;
  GETCHAR's -1/-2 are its own return space (so -2 = Ctrl+C there, not ENOSYS).
* **A8** — GETABI syscall 82h; osbkt checks the version at startup and the
  shell **refuses to run** when the kernel's GETABI is lower than the version
  it needs (E3). ABI.md and `Syscall.AbiVersion` bump together per slice.
* **A9** — drop the duplicate MEMINFO 73h row; MEMINFO is 10h only.
* **A10** — sibling-checkout convention documented in each repo's README;
  build.sh fails with a clear message when a sibling repo is missing.
* **A11** — golden transcripts: build the last commit before the legacy
  shell stopped being used (or a throwaway patched kernel in /tmp that calls
  `Shell.Run()`), capture per-command serial transcripts as fixtures;
  acceptance = automated QEMU serial scripts comparing output.
* **A12** — OSLANG stays in the kernel for this project; temporary loss of
  the `OSLANG` command and `.osl` scripts documented (§6).

Remaining open questions (to resolve during 1b/1c, not blocking 1a):

1. GETABI 82h — confirm the number is free after 80h/81h (it is; 82h is
   the next free AH).
2. crt0 startup symbol: each consumer links with `--defsym osb_startup=<mangled
   StartupCodeMain>`; the per-consumer symbol naming must be in ABI.md.
3. STAT out-record exact layout (32 B) — fixed in ABI.md v0 from the
   legacy `FStat` writer.
4. PASSWD/SUDOERS parsing: reuse the legacy `Users.cs` parser verbatim
   (presentation-free) inside the kernel, or rewrite — decide in 1c.