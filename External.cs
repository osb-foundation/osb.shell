using OsbKT;

namespace Osb.Shell.Userspace
{
    // External command lookup and spawn (Slice 3, C3).
    //
    // The first token is not an internal command: `.\name` runs from the
    // current directory, a token with a drive or a backslash is used as-is,
    // and otherwise each ';' entry of PATH is tried as <entry>\<name> and
    // <entry>\<name>.EXE. PATH entries that are not absolute (no drive letter
    // and no leading backslash) are ignored: a relative entry would resolve
    // against the current drive and directory, which can change between the
    // search and the spawn, so it is not a stable location. (The legacy shell
    // accepted relative entries; this is a deliberate, documented deviation.)
    //
    // The rest of the line is the argument string (128-byte ABI cap); the
    // legacy spawn-failure message is used for every refusal.
    internal static unsafe class External
    {
        private const int LineCap = 128;
        private const int PathCap = 260;

        public static void Run(char* line, int len)
        {
            int s = 0;
            while (s < len && line[s] == ' ') s++;
            int e = s;
            while (e < len && line[e] != ' ') e++;
            int nameLen = e - s;
            if (nameLen <= 0) return;

            var name = stackalloc byte[128];
            if (nameLen > 127) nameLen = 127;
            for (int k = 0; k < nameLen; k++) name[k] = (byte)line[s + k];

            int argS = e;
            while (argS < len && line[argS] == ' ') argS++;
            int argLen = len - argS;
            if (argLen > LineCap)
            {
                Syscall.PrintString("Bad command or file name.\r\n");
                return;
            }
            var args = stackalloc byte[LineCap];
            for (int k = 0; k < argLen; k++) args[k] = (byte)line[argS + k];

            // Explicit .OSL source file -> launch via OSLANG.EXE. Explicit
            // extensions are always respected: no internal-command lookup, no
            // PATH executable search. The argument string passed to OSLANG.EXE
            // is "<sourcePath> <appArgs>"; OSLANG.EXE parses the first token as
            // the source and the remainder as the application's ARGS.
            if (IsOsl(name, nameLen))
            {
                int r = SpawnOslang(name, nameLen, args, argLen);
                if (r >= 0) Syscall.Wait(r);
                else Syscall.PrintString("OSLANG: runtime not found\r\n");
                return;
            }

            var full = stackalloc byte[PathCap];
            int pid;

            if (nameLen >= 2 && name[0] == (byte)'.' && (name[1] == (byte)'\\' || name[1] == (byte)'/'))
            {
                var cwd = stackalloc byte[256];
                int cl = Syscall.GetCwd(cwd, 256);
                if (cl < 0) cl = 0;
                int n = 0;
                for (int k = 0; k < cl && n < 250; k++) full[n++] = cwd[k];
                for (int k = 2; k < nameLen && n < 250; k++) full[n++] = name[k];
                pid = SpawnResolved(full, n, args, argLen);
            }
            else if (HasSep(name, nameLen))
            {
                int n = nameLen < PathCap - 1 ? nameLen : PathCap - 1;
                for (int k = 0; k < n; k++) full[k] = name[k];
                pid = SpawnResolved(full, n, args, argLen);
            }
            else
            {
                pid = SearchPath(name, nameLen, args, argLen, full);
            }

            if (pid >= 0) { Syscall.Wait(pid); return; }
            if (pid == -3) Syscall.PrintString("Permission denied\r\n");
            else if (pid == -6) Syscall.PrintString("Out of memory.\r\n");
            else Syscall.PrintString("Bad command or file name.\r\n");
        }

        // Search PATH (GETENV) for name/name.EXE. Returns the pid, -4 when no
        // entry resolves, or another negative spawn error from the first entry
        // that did resolve.
        private static int SearchPath(byte* name, int nameLen, byte* args, int argLen, byte* full)
        {
            var pbuf = stackalloc byte[256];
            var pname = stackalloc byte[4];
            pname[0] = (byte)'P'; pname[1] = (byte)'A'; pname[2] = (byte)'T'; pname[3] = (byte)'H';
            int pl = Syscall.GetEnv(pname, 4, pbuf, 256);
            if (pl < 0) pl = 0;

            int i = 0;
            bool hasExt = HasExt(name, nameLen);
            while (i < pl)
            {
                int es = i;
                while (i < pl && pbuf[i] != (byte)';') i++;
                int el = i - es;
                i++;                       // skip ';'
                if (el <= 0) continue;
                bool abs = (el >= 2 && pbuf[es + 1] == (byte)':') || pbuf[es] == (byte)'\\';
                if (!abs) continue;        // relative entries are ignored (C3)
                // .OSL via OSLANG.EXE precedes bare/.EXE executables
                // (extensionless names only; current directory was already
                // tried before internal commands).
                if (!hasExt)
                {
                    int nn = BuildOslPath(full, pbuf + es, el, name, nameLen);
                    uint ino; ushort mode, uid; ulong size;
                    if (Syscall.ResolvePath(full, nn, out ino, out mode, out uid, out size))
                    {
                        int r = SpawnOslang(full, nn, args, argLen);
                        if (r >= 0) { Syscall.Wait(r); return r; }
                        return r;        // -4 runtime missing, or real spawn error
                    }
                }
                int n = BuildPath(full, pbuf + es, el, name, nameLen, false);
                int pid = SpawnResolved(full, n, args, argLen);
                if (pid != -4) return pid;
                n = BuildPath(full, pbuf + es, el, name, nameLen, true);
                pid = SpawnResolved(full, n, args, argLen);
                if (pid != -4) return pid;
            }
            return -4;
        }

        private static int BuildPath(byte* dst, byte* entry, int elen, byte* name, int nameLen, bool exe)
        {
            int n = 0;
            for (int k = 0; k < elen && n < 250; k++) dst[n++] = entry[k];
            if (n == 0 || dst[n - 1] != (byte)'\\') dst[n++] = (byte)'\\';
            for (int k = 0; k < nameLen && n < 250; k++) dst[n++] = name[k];
            if (exe && n < 256)
            {
                dst[n++] = (byte)'.'; dst[n++] = (byte)'E'; dst[n++] = (byte)'X'; dst[n++] = (byte)'E';
            }
            return n;
        }

        private static bool HasSep(byte* s, int n)
        {
            for (int k = 0; k < n; k++)
                if (s[k] == (byte)':' || s[k] == (byte)'\\' || s[k] == (byte)'/') return true;
            return false;
        }

        // Resolve path and spawn it with args (6Ah). Returns the pid, -4 when
        // the path does not resolve, or another negative error from the kernel.
        private static int SpawnResolved(byte* path, int plen, byte* args, int alen)
        {
            uint ino;
            ushort mode, uid;
            ulong size;
            if (!Syscall.ResolvePath(path, plen, out ino, out mode, out uid, out size)) return -4;
            var req = new Syscall.SpawnReq
            {
                PathPtr = (ulong)path,
                PathLen = (uint)plen,
                ArgsPtr = alen > 0 ? (ulong)args : 0,
                ArgsLen = (uint)alen,
                StdinHandle = -1,
                StdoutHandle = -1
            };
            return Syscall.SpawnPath(&req);
        }

        // True when the token carries an explicit extension (a '.' beyond the
        // first character). Extensionless tokens go through the .OSL-first
        // resolution policy.
        internal static bool HasExt(byte* name, int nameLen)
        {
            for (int k = 1; k < nameLen; k++)
                if (name[k] == (byte)'.') return true;
            return false;
        }

        // True when the token's extension (text after the last '.') is .OSL,
        // case-insensitive per OSB filesystem conventions.
        private static bool IsOsl(byte* name, int nameLen)
        {
            int dot = -1;
            for (int k = 0; k < nameLen; k++)
                if (name[k] == (byte)'.') dot = k;
            if (dot < 0 || dot + 1 >= nameLen) return false;
            if (nameLen - dot - 1 != 3) return false;
            return Commands.Upper((char)name[dot + 1]) == 'O'
                && Commands.Upper((char)name[dot + 2]) == 'S'
                && Commands.Upper((char)name[dot + 3]) == 'L';
        }

        // Build <entry>\<name>.OSL into dst; returns its length.
        private static int BuildOslPath(byte* dst, byte* entry, int elen, byte* name, int nameLen)
        {
            int n = 0;
            for (int k = 0; k < elen && n < 250; k++) dst[n++] = entry[k];
            if (n == 0 || dst[n - 1] != (byte)'\\') dst[n++] = (byte)'\\';
            for (int k = 0; k < nameLen && n < 250; k++) dst[n++] = name[k];
            dst[n++] = (byte)'.'; dst[n++] = (byte)'O'; dst[n++] = (byte)'S'; dst[n++] = (byte)'L';
            return n;
        }

        // Spawn OSLANG.EXE to interpret an .osl source file. Builds the
        // argument string "<sourcePath> <appArgs>" (B1, 128-byte ABI cap);
        // OSLANG.EXE parses the first token as the source and the remainder
        // as the application's ARGS. Searches PATH for OSLANG.EXE. Returns
        // the child pid (>= 0) or -4 when the runtime is not found. Does not
        // print; the caller reports failure and waits for the child.
        private static int SpawnOslang(byte* source, int sourceLen, byte* args, int argLen)
        {
            var abuf = stackalloc byte[128];
            int n = 0;
            for (int k = 0; k < sourceLen && n < 127; k++) abuf[n++] = source[k];
            if (n < 127 && argLen > 0)
            {
                abuf[n++] = (byte)' ';
                for (int m = 0; m < argLen && n < 127; m++) abuf[n++] = args[m];
            }

            var pname = stackalloc byte[10];
            pname[0] = (byte)'O'; pname[1] = (byte)'S'; pname[2] = (byte)'L'; pname[3] = (byte)'A';
            pname[4] = (byte)'N'; pname[5] = (byte)'G'; pname[6] = (byte)'.'; pname[7] = (byte)'E';
            pname[8] = (byte)'X'; pname[9] = (byte)'E';
            var full = stackalloc byte[PathCap];
            return SearchPath(pname, 10, abuf, n, full);
        }

        // Try <cwd>\<name>.OSL; if present, launch it via OSLANG.EXE. The
        // current directory always wins for .OSL applications, including over
        // internal Shell commands, so this is consulted before command-table
        // dispatch. Returns true when handled (whether or not the runtime was
        // found), false when no <cwd>\<name>.OSL exists.
        internal static bool TryOslInCwd(byte* name, int nameLen, byte* args, int argLen)
        {
            var cwd = stackalloc byte[256];
            int cl = Syscall.GetCwd(cwd, 256);
            if (cl < 0) cl = 0;
            var full = stackalloc byte[260];
            int n = 0;
            for (int k = 0; k < cl && n < 250; k++) full[n++] = cwd[k];
            if (n > 0 && full[n - 1] != (byte)'\\' && full[n - 1] != (byte)':')
                full[n++] = (byte)'\\';
            for (int k = 0; k < nameLen && n < 250; k++) full[n++] = name[k];
            full[n++] = (byte)'.'; full[n++] = (byte)'O'; full[n++] = (byte)'S'; full[n++] = (byte)'L';
            uint ino; ushort mode, uid; ulong size;
            if (!Syscall.ResolvePath(full, n, out ino, out mode, out uid, out size)) return false;
            int pid = SpawnOslang(full, n, args, argLen);
            if (pid >= 0) Syscall.Wait(pid);
            return true;
        }
    }
}