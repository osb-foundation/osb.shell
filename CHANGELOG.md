# Changelog

## [0.1.0] - Olorun (Generation 0)

### Added
- Initial release of osb.shell under OSB generation 0 (Olorun).
- Freestanding userspace shell (SHELL.EXE) implementing a REPL with OsbKT console I/O and file operations.
- Ring-3 program staging (built with `WITH_TESTS=1`): TSpawn, TExit, TArgs, TPrint, TWait, TPtr, TSpawnErr, TQuiet, TId, TSpin, TLogout, TMemGrow, TMemProvider, TMkdir, TEnv, TFileIO, TFileManip, TPath, TSudo, TTlsProof, TAlloc, TOslangProof, TReferenceLexer, TStringBuilder, TStringLayout, TEETypeLayout, TOslangEntry.
- Test proofs: THello (Ring-3 Hello World, exit 42), TNX (EACCES non-exec mode 0100644), ROOTONLY (0700 root-only exec), TQuiet (0600 silent copy).
- `VER` command reads component versions and the Olorun codename from the generated `/OSB/CONF/VERSION.JSON` artifact (no hardcoded version table); supports `/O /S /C /T /L /A` flags.
- Compiled as a native AOT executable (SHELL.EXE) placed at /OSB/BIN/SHELL.EXE.

### Stable
- Build succeeds (`dotnet build -c Release Osb.Shell.Userspace.csproj`); host unit tests pass (`tests/run.sh unit`, 52/52); ELF links correctly; ABI version constant verified (ABI v8).

### Experimental / Incomplete
- Full VGA/graphical desktop not implemented; gfxterm baseline only.
- Userspace shell depends on OsbKT ABI; ring-3 programs require `WITH_TESTS=1`.