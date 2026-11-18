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
            while (i < pl)
            {
                int es = i;
                while (i < pl && pbuf[i] != (byte)';') i++;
                int el = i - es;
                i++;                       // skip ';'
                if (el <= 0) continue;
                bool abs = (el >= 2 && pbuf[es + 1] == (byte)':') || pbuf[es] == (byte)'\\';
                if (!abs) continue;        // relative entries are ignored (C3)
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
    }
}