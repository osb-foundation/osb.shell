# Changelog

## [0.1.0] - Olorun (Generation 0)

### Added
- Initial release of osb.shell under OSB generation 0 (Olorun).
- Freestanding userspace shell (SHELL.EXE) implementing a REPL with OsbKT console I/O and file operations.
- Ring-3 program staging (TSpawn, TExit, TArgs, TPrint, TWait, TPtr, TSpawnErr, TQuiet, TId, TSpin, TLogout, TMemGrow, TMemProvider, TMkdir, TOslangProof, TReferenceLexer, TStringLayout, TMemProvider, TEETypeLayout, TOslangEntry, TOslangProof, TPath, TFileIO, TFileManip, TPath, TMemGrow, TTlsProof, TAlloc, TOslangProof, TReferenceLexer, TStringBuilder, TStringLayout, TSudo, TTlsProof, TWait, TPath, TPrint, TPath, TPath).
- Test proofs: THello (Ring-3 Hello World, exit 42), TNX (EACCES non-exec mode 0100644), ROOTONLY (0700 root-only exec), TQuiet (0600 silent copy).
- Compiled as native AOT executable (SHELL.EXE) placed at /OSB/BIN/SHELL.EXE.

### Stable
- Build succeeds (`dotnet build -c Release Osb.Shell.Userspace.csproj`); runs host unit tests (`tests/unit/Osb.Shell.Unit.csproj`); ELF links correctly; ABI version constant verified (ABI v8).

### Experimental / Incomplete
- Full VGA/graphical desktop not implemented; gfxterm baseline only.
- Userspace shell depends on OsbKT ABI; ring-3 programs require WITH_TESTS=1.
