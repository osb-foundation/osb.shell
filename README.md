# OSB Shell (Userspace)

A freestanding userspace shell for OSB (Operating System Basic).

## Overview

`osb.shell` is the userspace shell for OSB. It is a simple REPL (Read-Eval-Print Loop) that runs on top of the OSB kernel, using the `osbkt` library for console I/O and file operations. It is compiled as a freestanding NativeAOT executable (`SHELL.EXE`) and placed on the ext4 image at `/OSB/BIN/SHELL.EXE`.

## Features

- **Interactive REPL** - Command-line interface with prompt
- **File Operations** - Create, read, write, delete files
- **Directory Navigation** - Change directories, list contents
- **Program Execution** - Spawn and wait for external programs
- **Environment Variables** - Get and set environment variables

## Architecture

The shell is built on top of:
- **osbkt** - INT 21h syscall wrappers for I/O and file operations
- **osb.core** - Shared runtime library (MiniCoreLib)
- **OSB Kernel** - Provides the underlying OS services

## Building

```bash
dotnet build
```

This produces `OsbShell.exe` (SHELL.EXE) that can be loaded by the OSB kernel.

## Part of OSB

This is part of the [OSB](https://github.com/osb-system) - Operating System Basic project, a DOS-like operating system built from scratch.

## License

MIT License - see [LICENSE](LICENSE) for details.