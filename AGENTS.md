# AGENTS.md — OSB Shell (Userspace)

This file guides coding agents working in the `osb.shell` repository.

## Project Identity

**OSB Shell (Userspace)** is a freestanding userspace shell for OSB (Operating System Basic). It is a simple REPL that runs on top of the OSB kernel, using the `osbkt` library for console I/O and file operations.

## Code Policy

1. **Implementation Language**
   - Use **C#** for shell logic and REPL implementation.

2. **Freestanding Constraints**
   - No BCL convenience, no GC allocations, no exceptions for normal control flow.
   - Fixed/stack buffers where appropriate.
   - Deterministic, bounded behavior over convenience frameworks.

3. **Dependencies**
   - Depends on `osbkt` for INT 21h syscall wrappers (linked sources:
     `../osbkt/Syscall.cs` — sibling checkout required, see README.md).
   - Depends on `osb.core` for the canonical MiniCoreLib (linked from
     `../osb.core/src/MiniCoreLib.cs` — never keep a local copy).
   - Communicates with OSB kernel via syscalls (`osbkt/docs/ABI.md` is the
     contract).

## Architecture

The shell provides:

- **REPL Loop** - Interactive command prompt
- **Command Execution** - Run built-in commands and external programs
- **File Operations** - Create, read, write, delete files
- **Directory Navigation** - Change directories, list contents
- **Environment Management** - Get/set environment variables

## Build and Validation

```bash
dotnet build
```

Validation expectations:

- Build succeeds.
- Binary is produced (OsbShell.exe / SHELL.EXE).
- Can be loaded by the OSB kernel.

After `../osb.kernel/build.sh`:

```bash
tests/smoke.sh                # QEMU serial smoke test (banner, headless)
tests/golden/golden.sh        # golden transcripts vs the legacy shell (A11)
```

## Change Workflow for Agents

1. Inspect relevant files first.
2. Keep edits targeted.
3. Do not break compatibility with osbkt or the OSB kernel.
4. Check file lengths before finishing (no file may exceed 400 lines).

## Non-Goals and Cautions

- Do not convert shell to a Unix-like shell.
- Do not introduce host-OS runtime dependencies.
- Do not break compatibility with the OSB kernel's INT 21h ABI.

## Documentation Anchors

- `README.md`: High-level project/user documentation.