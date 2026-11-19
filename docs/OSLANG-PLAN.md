# OSLANG-PLAN — OSLANG as a first-class Ring-3 citizen (instructions for Kilo)

Read `OSB-ARCHITECTURE.md` first (especially §2, §7, §8, §12, §13). Where this plan and that
document disagree, stop and ask.

## 0. Goal and decisions (all DECIDED by the owner)

1. `OSLANG.EXE` runs in **Ring 3** as a separate executable in `A:\OSB\BIN`.
2. It implements **everything in the OSLANG 0.73 spec** (`osb.oslang/OSLANG-SPEC.md` plus the
   per-version specs it links), as an interpreter for OSB.
3. **No .NET BCL** (no `System.IO/Text/Xml/Linq/Collections.Generic/Math/DateTime`...).
   Freestanding rules apply: no GC allocations from C#, no exceptions for ordinary control
   flow, fixed/stack buffers, **max 400 lines per file** (AGENTS.md of every repo).
4. **Network features are out of scope** (`OSB.NET`, HTTP). They must exist in the language
   surface and fail with a defined, catchable OSLANG runtime error ("not supported on this host").
5. The in-kernel OSLANG port is **removed completely** (phase O0).
6. `.OSL` files run directly: `Hello.osl` ≡ `OSLANG Hello.osl`. Typing `Hello` also works when
   no `Hello.EXE` is found but `Hello.OSL` is.

## 1. Proposed defaults — keep unless I say otherwise

* **D1 One engine, two targets.** A single engine source tree compiled (a) for the host with
  normal .NET (fast conformance tests) and (b) freestanding for OSB. Only the *host layer*
  (console, files, time, memory) differs. The existing BCL implementation (`Osb.Lang`) stays
  as reference/oracle until parity, then I decide whether to archive it.
* **D2 Memory.** The engine owns its heap: tagged values, precise non-moving mark-sweep GC,
  roots in an explicit value/frame stack (never raw heap pointers in C# locals across an
  allocation). Strategy: bump arena first (O3b), GC in O3c, but design the roots for GC from day one.
* **D3 Strings.** Proposal: immutable, length-prefixed UTF-8 in the heap; decide the indexing
  unit and case-folding rules from the spec in O0 and document them in `ENGINE-DESIGN.md`.
* **D4 Association rules.** The shell runs `A:\OSB\BIN\OSLANG.EXE <script> <args>`. The
  interpreter path is **fixed** (never via `PATH`, so `SUDO x.osl` cannot be hijacked).
  The script needs **read** permission only (the interpreter opens it as the caller; the
  kernel enforces). Explicit `.osl` extension → interpreter. No extension → `.EXE` first, then `.OSL`.
  Script exit code = process exit code.
* **D5 Spec is the truth.** Conformance tests are derived from the spec, tagged by spec
  section. `Osb.Lang` and the kernel port are references, not oracles: where they disagree
  with the spec, the spec wins. Ambiguities go to `osb.oslang/docs/SPEC-QUESTIONS.md` — do
  **not** guess; I answer them.

## 2. Preconditions (stage B cleanup)

Do **not** start O1 unless these are done. If they are not, stop and tell me; do not work
around them.

* File syscalls 26h–29h/61h/62h rewritten as path-based primitives with real error codes (no
  command-line parsing, no VGA output, no always-zero returns).
* `ABI.md` version == `Syscall.AbiVersion` == kernel `AbiVersion`, with a CI/script check.
* No file you touch exceeds 400 lines.
* READMEs of the touched repos describe what exists.

(O0 may start without them.)

## 3. Phases — each ends with a STOP for my review

Per phase: commit per repo with focused messages; update docs in the same commit as code;
report build results per repo, test results, anything you could not verify, open questions.
**Do not start the next phase.**

### O0 — Design, repo layout, kernel port removal (no engine code)

1. Read the full spec set. Write `osb.oslang/docs/ENGINE-DESIGN.md`: allowed C# subset under
   `MiniCoreLib` (verify by compiling a probe program freestanding and booting it in QEMU:
   interfaces/virtual calls, generics, delegates, structs, `stackalloc`, string literals,
   `switch`), value/heap/GC design (D2), string model (D3), module layout, host-layer API,
   OSLANG-exception model without C# exceptions (error codes + unwinding in the interpreter),
   recursion/stack limits, memory budget. **Measure the NativeAOT binary size** of the probe
   and extrapolate.
2. Write `docs/SPEC-QUESTIONS.md` with every ambiguity found.
3. Restructure `osb.oslang`: `engine/`, `host/` (`Host.Test`, `Host.Osb`), `lib/` (OSL.* libraries),
   `os/` (`OSLANG.EXE` entry), `tests/conformance/`, `samples/`, `archive/`. Existing `Osb.Lang`
   moves to `reference/` and keeps building. Fix the stale `src/…` paths in docs/csproj.
4. **Remove the kernel port:** copy `osb.kernel/Oslang*.cs` (and any OSL-only notes) to
   `osb.oslang/archive/kernel-port/` (not built, read-only reference), then delete them from the
   kernel; move `osb.kernel/seed/apps/*.osl` to `osb.oslang/samples/seed/`; drop the `.osl`
   staging from `osb.kernel/build.sh` for now; remove `OSLANG`/`KISS` from the shell command
   table and HELP; fix kernel README/ROADMAP mentions. Kernel builds, boots, smoke test passes.
5. Version housekeeping: `OslangInterpreter.Version`, README, spec headers say one thing
   (0.73 where the code is not yet 0.73: record the gap in `OSLANG-GAP.md`).

**Acceptance:** kernel has zero OSLANG code; builds and smoke test green; ENGINE-DESIGN.md
and SPEC-QUESTIONS.md complete; probe measurements reported.
**STOP.**

### O1 — Platform prerequisites (design, then implement)

Everything here is **ABI.md first**, then kernel + `osbkt` + tests, one syscall group per commit.

* **K1 Growable memory.** Today a process has one fixed 2 MiB block holding image, heap and
  stack, and no syscall grows it. Design a syscall (ABI.md entry) granting memory in 2 MiB
  steps up to a per-process limit, accounted and released at reap, with `EFAULT`-safe
  validation of the new range. Test: a ring-3 program grows to the limit, touches every page,
  exits; frames return (`MEMINFO` before/after); a second process is unaffected.
* **K2 Terminal API** for `OSL.CONSOLE`/`OSL.APP` (spec 0.7+, `KEYCODE` in 0.73): screen size,
  get/set cursor position, show/hide cursor, per-write colors (existing `SETCOLOR`),
  **raw key events** (enter/esc/tab/backspace/delete/insert/arrows/home/end/pgup/pgdn/F1–F12,
  modifiers), non-blocking, from PS/2 **and** serial (decode `ESC[`/`ESC O` sequences).
  Derive the exact requirement list from the spec; do not invent features.
* **K3 File/dir I/O audit** against `OSL.FILE`/`OSL.DIR`: open for read/write/append/create,
  read, write, seek, close, stat, list, mkdir, delete, rename, cwd, with permission checks
  kernel-side and real error codes. Produce a gap table first.
* All errors use the ABI error table; no presentation in the kernel.

**Phase O1a:** design only (ABI.md entries + gap table). **STOP.** Then **O1b:** implement.
**STOP.**

### O2 — Conformance corpus and host runner

* `tests/conformance/<area>/<name>.osl` + `.out` (+ `.in` for input, + `tags` line:
  spec section ids and `needs-net`/`needs-terminal`).
* Cover the whole spec, ordered by version (0.1 … 0.73), including every error/exception case
  and the 0.72/0.73 breaking changes (`KEYCODE`, enum registration, `ME` shadowing,
  identifier resolution order). Expected outputs derived from the spec, never copied from an
  implementation.
* A host runner (`dotnet run`) and an OSB runner (QEMU, reusing the serial driver in
  `osb.shell/tests/golden`) that diff output. Start with a trivial engine stub so the runners work.
* Run the corpus against `reference/Osb.Lang`; produce `docs/ORACLE-REPORT.md` listing where
  the reference deviates from the spec. Do not fix the reference.

**Acceptance:** corpus size and spec-section coverage table; runners work end to end on a stub.
**STOP.**

### O3 — Engine core (host + OSB)

* **O3a** lexer, parser, AST (arena-allocated). `--dump-ast` output tested. STOP.
* **O3b** values, strings, numbers (own formatting/parsing/double math), arrays, variables,
  operators, control flow, functions, recursion limit, `PRINT`/`INPUT`/`CLEAR`, `ARGS`, with the arena heap. STOP.
* **O3c** GC (D2) with stress tests: long loops allocating strings/arrays/objects must run in
  bounded memory; no collection-time corruption (poison-fill freed memory in test builds). STOP.
* **O3d** OOP: classes, inheritance, interfaces, visibility, `ME`/`BASE`, constructors,
  virtual/override/abstract/static, generics, overloading, events, closures/lambdas. STOP.
* **O3e** exceptions (`TRY/CATCH`, runtime errors with source locations), enums/enum sets/flags,
  `SWITCH`, `FOR IN/EACH`, `USING`, multi-file programs/modules. STOP.

Each sub-phase: conformance subset passes on host **and** in QEMU; line limit respected;
memory use reported.

### O4 — Standard library

`OSL.MATH` (no `System.Math`), primitive methods and collections, `DATE`/`TIME` (own calendar
code over the `DATETIME`/`TICKS` syscalls), `OSL.FILE`/`DIR` over K3, `OSL.JSON`, `OSL.CSV`,
`OSL.XML`, `OSL.CNF`, `OSL.I18N` — all with hand-written parsers/serialisers. `OSB.NET`/HTTP
stubs raise the defined "not supported" error. **STOP** after the library is complete.

### O5 — Full-screen terminal apps

`OSL.CONSOLE`/`OSL.APP`, keyboard events and `KEYCODE` over K2, as in spec 0.7–0.73. Include a
small interactive sample app and a scripted serial test. **STOP.**

### O6 — Integration

* `OSLANG.EXE` entry: argument parsing, script load via `osbkt`, error reporting with line
  numbers, exit codes, Ctrl+C handling, memory-limit errors that are clean OSLANG errors.
* `build.sh`: build/stage `OSLANG.EXE` into `A:\OSB\BIN` (mode 0755), stage samples; `osb.oslang`
  becomes a required sibling with the existing clear-failure check.
* Shell: `.OSL` resolution per D4 (fixed interpreter path, read-only requirement), HELP text,
  background `&`, `SUDO x.osl`, argument passing (128-byte cap documented; report if it hurts).
* Golden/QEMU cases: run samples directly and via `OSLANG`, permission denied on an unreadable
  script, missing interpreter message, exit code propagation, a script cannot gain privileges
  by issuing a raw syscall, killing a script, two scripts concurrently.
* If a `KISS.OSL` exists anywhere in the org, use it as an end-to-end acceptance app.

**STOP.**

### O7 — Closure

Run the whole corpus on host and QEMU; performance and memory report (script startup time,
binary size, heap peak for the sample set); update `OSB-ARCHITECTURE.md` §3/§12/§14/§16,
OSLANG-GAP, VS Code extension version note, READMEs; list what remains (network). Propose what
to do with `reference/Osb.Lang`. **STOP.**

## 4. Rules (all phases)

* Obey every repo's AGENTS.md: freestanding, no BCL in OSB-target code, no GC allocations
  from C#, **≤ 400 lines per file**, ABI.md in lockstep with every syscall change.
* Never reintroduce OSLANG code into the kernel. The kernel gets syscalls, not interpreter logic.
* The kernel validates everything; the engine/SDK is not a trust boundary.
* No speculative features: the spec decides. Ask via `SPEC-QUESTIONS.md`.
* Do not delete the reference implementation or the archive without my say-so.
* Report honestly what you could not run or verify.

## 5. Report format (end of every phase)

1. What was done, per repo, with commit hashes.
2. Build results per repo; test results (host and QEMU) with counts.
3. Metrics: file-length check, binary size, memory use.
4. Deviations from this plan and why.
5. Not verified.
6. Open questions for me.
