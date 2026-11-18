# Test timing (Phase 3, Part B — B0 measurement and B1/B2 wins)

Measured on this host: 8 cores, `/dev/kvm` present (nested), QEMU 8.2.2,
`qemu-system-x86_64`, TCG default. Times are wall clock of the harness, not of
the guest; the host runs other apps (Vivaldi/Codium) so absolute numbers vary
by ~2-3x with load. The before/after ratios are the meaningful part.

## B0: what dominates

| stage | before | after | change |
|---|---|---|---|
| `osb.kernel/build.sh` (incremental) | ~11 s idle | ~11 s idle (25-84 s loaded) | B3: no cache (see below) |
| image copy per case (`cp osb.img`) | ~10 ms | ~10 ms | 16 MB, page-cached |
| `patch_boot` (debugfs + e2fsck) | ~0.4 s | ~0.4 s | only `boot` cases |
| QEMU boot to login (TCG) | ~1.0 s | ~1.0 s | kernel, not harness |
| `drive.py` init wait | 0.8 s (login) / 12 s (nologin) | 0.8 s / **0 s** | B2: skip for `nologin` |
| `drive.py` close (drain + QEMU exit) | 3.7 s | **0.3 s** | B2: monitor `quit` |
| VGA `settle` + dump | 0.35 s + ~0.5 s per dump | same | monitor read without fixed sleep |
| golden suite, 20 cases, `-j 1` | 203 s (TCG) / 268 s (KVM) | **~28 s idle / 72 s loaded** | B1+B2 |
| golden suite, 20 cases, `-j 4` | — | **15 s idle / 33-39 s loaded** | B1 parallel |

Per-case `drive.py` before → after:

| case | before | after | why |
|---|---|---|---|
| `05-dir.in` (login) | 4.5 s | 1.2 s | close 3.5→0.3 s |
| `23-print.scm` (nologin + boot) | 15.2 s | 1.2 s | init 12→0 s, close 3.2→0.3 s |
| `24-ptr.scm` (nologin) | 15.2 s | 1.2 s | same |
| `31-edit-wrap.scm` (6 VGA dumps) | 4.4 s | 4.2 s | settle-bound; monitor sleep removed |
| `33-complete-wrap.scm` (2 dumps) | 10.6 s | 5.0 s | init skip; faster monitor |

## Accelerator: KVM vs TCG (B0)

`bench.py` (fresh image copy, QEMU to first serial byte, to `login:`, to prompt,
script, shutdown; n=5 KVM, n=3 TCG):

| phase | KVM | TCG |
|---|---|---|
| qemu start → first serial byte | 0.480 s | 0.869 s |
| first serial byte → `login:` | **3.431 s** | **0.217 s** |
| login → prompt | 0.072 s | ~0.05 s |
| script | 0.011 s | ~0.01 s |
| shutdown (old close) | 0.032 s | ~0.03 s |
| total | **4.05 s** | **1.16 s** |

KVM is ~3.5x slower *here*. Root cause: the guest is ATA-PIO bound; every
`inw`/`inb` is a VM exit, and the shell load alone is ~10K 16-bit transfers
(plus ext4 metadata). Under TCG the I/O is serviced in-process without exits.
`Ata.WaitBusy`/`WaitDrq` are unbounded spin loops, so the cost scales with the
PIO count. On bare metal KVM would likely win; on this nested host it does not.

Decision: `drive.py` defaults to **TCG**; `OSB_ACCEL=kvm` forces KVM
(`-enable-kvm -cpu host -smp 1`). This is a deliberate, measured deviation
from "force KVM" — the point of B is wall-clock test speed.

## B1/B2: harness changes

- **B2** `drive.py`: skip the boot init wait for `nologin` scripts (they never
  show a prompt; the first `expect` consumes the boot output). Previously every
  `nologin` case burned the full 12 s `BOOT_DELAY`.
- **B2** `drive.py` `close()`: send `quit` on the QEMU monitor instead of
  closing stdin and waiting out the 3 s SIGTERM timeout. Fallback: terminate/kill.
- **B2** `vga.py`: drain the monitor banner/prompt before sending a command and
  read until the next `(qemu)`; no fixed `sleep(0.12)` per command. Correct and
  slightly faster.
- **B1** `golden.sh -j N`: per-case temp dirs (image copy, BOOT patch,
  transcript, monitor socket all private), at most N concurrent QEMUs, results
  buffered and printed in case order (deterministic output). Default
  `nproc/2`; `-j 1` is the old sequential behavior.
- **B2** `16-jobs.scm`: `sleep 2` → `expect Foreground [` (DEMO is already the
  foreground task once the shell prints that line). `34-escape.scm` keeps its
  `sleep 0.4`: it is the measurement that distinguishes the 5-tick ESC wait
  from the old 100-tick wait, not a timeout.

## B3: build timing (evaluated, no cache)

Incremental `WITH_TESTS=1 ./build.sh` on an idle host: **10.9 s** total.
Stage breakdown from the build log: ext4 image generation ~0.5 s, `dotnet build`
(kernel) 4.7 s, `ilc` NativeAOT the bulk of the remaining ~3 s, link <0.2 s,
`stage_ring3` (SHELL.EXE + test programs) 2.4 s, `grub-mkrescue` ~2 s.
A content-hash cache for `ilc`/ring-3 could save ~3-4 s, but the cache key must
include the compiler version and every linked source; with the suite at
15-39 s the payoff is small. Evaluated, not implemented.

## B4: mutation suite (baseline; parallel deferred)

`tests/mutations.sh`: **11/11 caught, 0 missed, 0 broken**, **15m21s** on this
host (each patch: rebuild ~11 s + the case, which can hit drive.py's 25 s
timeout when the mutation makes the expected line disappear). The golden-side
parallelism (B1) is the main wall-clock win; parallelizing mutations needs a
git worktree per worker plus symlinked sibling repos (`build.sh` expects
`../osb.shell`, `../osbkt`, `../osb.core`) and is deferred.

## B5: legacy parity (hash gate)

`tests/golden/parity.sh`: skips with the input hash (drive.py, lib.sh,
`legacy/*.out`) stored in `legacy/.parity.stamp`; `--force` re-runs;
`--repeat N` defaults to 1. With no legacy ISO it prints
`SKIP: no legacy ISO` and exits 0 — there is no legacy ISO on this machine
(`find` finds only `osb.kernel/osb.iso`), so the parity cannot be re-run here.

## B7: host unit tests

`tests/unit` (net10.0 console app, no NuGet): links the real
LineEditor/Completion/Commands/History and replaces only the INT 21h layer
(scripted GetChar, fake ext4 tree, 80x25 console model with wrap-aware `\b`).
**52 assertions, 0 fail**: typing, backspace at end/mid, arrows/Home/End/Delete,
history, Ctrl+C, CRLF swallow, 80-column wrap + wrap-backspace, canonical
completion, ambiguous listing, file completion (single/no-match/root/empty
prefix). Wall clock: ~0.1 s test + ~4 s `dotnet build`; `tests/run.sh unit`
~4.3 s.

## B8: tiers

`tests/run.sh [smoke|unit|level|full]` (default `level`). `level` runs
`golden.sh -j nproc/2`; `full` adds `mutations.sh`. `smoke` is the default-image
smoke (login + H-B CONF/BOOT), `unit` is the host suite. `level`/`full` need a
`WITH_TESTS=1` ISO; `smoke` needs the default ISO (H-B requires the kernel to
ignore CONF/BOOT).

## B9: equivalence (green)

- Old harness (phase-2 `golden.sh` + `drive.py` + `lib.sh`) on the clean
  WITH_TESTS ISO with the current cases: **15/15 common cases PASS**. The 5 new
  Part-A cases (31-35) fail on the old driver only because it has no `vga`
  directive; they are new and not part of the old suite.
- New harness: **20/20 PASS, 0 FAIL, 17 SKIP**.
- 10 consecutive `-j 4` runs: all `pass=20 fail=0 skip=17` (33-39 s each under
  host load; 15 s idle).
- Mutations: **11/11 caught**.
- Unit: **52/52**.
- Smoke (default ISO): **PASS** login + **PASS** H-B.

## Final wall clock (this host)

| suite | before | after |
|---|---|---|
| golden, 20 cases, `-j 1` | 203 s (TCG) / 268 s (KVM) | ~28 s idle / 72 s loaded |
| golden, 20 cases, `-j 4` | — | 15 s idle / 33-39 s loaded |
| mutations | 15m21s (baseline) | 15m21s (unchanged; parallel deferred) |
| unit | — | 4.3 s (incl. build) |
| smoke | 45 s timeout | 17 s with `OSB_TIMEOUT=8` |