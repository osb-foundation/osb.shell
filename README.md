# OSB Shell (Userspace)

A freestanding userspace shell for OSB (Operating System Basic).

## Overview

`osb.shell` is the userspace shell for OSB. It is compiled as a freestanding NativeAOT executable (`SHELL.EXE`) and placed on the ext4 image at `/OSB/BIN/SHELL.EXE`, where the kernel spawns it at boot.

Current status (**phase 1a**): the shell verifies the kernel ABI (`GETABI`),
prints a banner, and idles. Interactive input, the line editor, and commands
arrive slice by slice — the authoritative plan and per-slice acceptance tests
are in [`docs/MIGRATION.md`](docs/MIGRATION.md).

## Features

- **ABI-checked startup** - refuses to run against an incompatible kernel
- **Zero-allocation core** - no GC, no BCL, fixed buffers only
- *(planned)* REPL with line editor, history and completion
- *(planned)* file operations, directory navigation, program execution
- *(planned)* environment variables, users/login, pipes/GREP, network tools

## Architecture

The shell is built on top of:
- **osbkt** - INT 21h syscall wrappers, crt0 startup, user.ld, mkosbx.py
  (consumed by **linked sources** — no DLLs at runtime)
- **osb.core** - canonical MiniCoreLib (linked from `src/MiniCoreLib.cs`)
- **OSB Kernel** - provides the underlying OS services

**Dependency model (verified):** osbkt is consumed by linked sources, not by a
`ProjectReference`. A `ProjectReference` to `osbkt` was tested and is
incompatible with this freestanding NoStdLib/MiniCoreLib arrangement (`csc`
cannot import predefined types from a referenced NoStdLib library, and `osbkt`
itself requires `MiniCoreLib` to compile `Syscall.cs`). Each consuming program
compiles the single canonical `MiniCoreLib.cs` (from `osb.core`) plus the
`osbkt` syscall wrappers as linked sources into one freestanding NativeAOT
module — no `osbkt.dll` ships at runtime. See `AGENTS.md`.

## Building

The OSB repos are expected **side by side** (sibling checkouts):

```
osb/ ├─ osb.kernel/  (build.sh produces osb.iso, stages SHELL.EXE)
     ├─ osb.shell/    (this repo)
     ├─ osbkt/
     └─ osb.core/
```

```bash
dotnet build              # this repo alone (needs the siblings above)
../osb.kernel/build.sh    # full ISO, includes SHELL.EXE
```

The build fails with a clear message if a sibling repo is missing.

## Testing

```bash
tests/smoke.sh      # boot the ISO headless; PASS = userspace banner on serial
tests/golden/golden.sh   # diff serial transcripts vs the legacy shell (A11);
                         # phase 1a skips (no input yet), gates from phase 1b
tests/golden/capture.sh <legacy-iso>   # regenerate goldens from a legacy build
```

## Part of OSB

This is part of the [OSB](https://github.com/osb-foundation) - Operating System Basic project, a DOS-like operating system built from scratch.

## License

MIT License - see [LICENSE](LICENSE) for details.