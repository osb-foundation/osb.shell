using OsbKT;

namespace Osb.Shell.Userspace
{
    // Slice 2 command table (S2.4). A fixed, NUL-free table of command names and
    // their dispatch ids, filled once from literals (the freestanding runtime
    // has no managed arrays, so this mirrors the legacy Completion.InitCommands
    // fixed-struct approach). The same table drives:
    //   * dispatch: Identify() maps the first token to an id (case-insensitive)
    //   * completion: Count/NameAt/NameMatches enumerate the names
    // Commands that are not implemented yet still appear (id NONE) so TAB can
    // complete them; dispatch treats NONE as "known but not implemented".
    internal static unsafe class Commands
    {
        public const int NameCap = 12;
        public const int Max = 64;

        // Dispatch ids. NONE = a known command that is not implemented yet.
        public const int NONE = 0;
        public const int EXIT = 1;
        public const int WHOAMI = 2;
        public const int LOGIN = 3;
        public const int LOGOUT = 4;
        public const int SUDO = 5;
        public const int HISTORY = 6;
        public const int ECHO = 7;

        private struct Table
        {
            public fixed char names[Max * NameCap];
            public fixed int lens[Max];
            public fixed int ids[Max];
            public int n;
        }

        private static Table _t;
        private static bool _ready;

        public static int Count => _t.n;

        public static void Init()
        {
            if (_ready) return;
            _ready = true;
            // Full DOS-like command set (legacy parity) for completion. Only the
            // 1c commands have real ids; the rest complete but are not run yet.
            Add("VER", NONE); Add("ABOUT", NONE); Add("HELP", NONE); Add("CLS", NONE);
            Add("CLEAR", NONE); Add("MEM", NONE); Add("HALT", NONE); Add("POWEROFF", NONE);
            Add("SHUTDOWN", NONE); Add("EXIT", EXIT); Add("QUIT", NONE); Add("REBOOT", NONE);
            Add("TIME", NONE); Add("DATE", NONE); Add("TICKS", NONE); Add("SLEEP", NONE);
            Add("ECHO", ECHO); Add("SET", NONE); Add("DIR", NONE); Add("CD", NONE);
            Add("PWD", NONE); Add("TYPE", NONE); Add("DEL", NONE); Add("ERASE", NONE);
            Add("DELETE", NONE); Add("MD", NONE); Add("MKDIR", NONE); Add("RD", NONE);
            Add("RMDIR", NONE); Add("REN", NONE); Add("RENAME", NONE); Add("MOVE", NONE);
            Add("COPY", NONE); Add("CP", NONE); Add("SIZE", NONE); Add("COLOR", NONE);
            Add("LN", NONE); Add("LNK", NONE); Add("OSLANG", NONE); Add("DEMO", NONE);
            Add("TASK", NONE); Add("PS", NONE); Add("KILL", NONE); Add("WAIT", NONE);
            Add("TAB", NONE); Add("SPAWN", NONE); Add("RING3", NONE); Add("TRASH", NONE);
            Add("GREP", NONE); Add("PING", NONE); Add("PCI", NONE); Add("NET", NONE);
            Add("IFCONFIG", NONE); Add("HISTORY", HISTORY); Add("USER", NONE);
            Add("LOGIN", LOGIN); Add("LOGOUT", LOGOUT); Add("WHOAMI", WHOAMI);
            Add("CHOWN", NONE); Add("CHMOD", NONE); Add("SUDO", SUDO);
        }

        private static void Add(string s, int id)
        {
            int i = _t.n;
            if (i >= Max) return;
            int len = s.Length;
            if (len > NameCap) len = NameCap;
            fixed (char* p = s)
            fixed (char* b = _t.names)
            {
                for (int k = 0; k < len; k++) b[i * NameCap + k] = p[k];
            }
            _t.lens[i] = len;
            _t.ids[i] = id;
            _t.n = i + 1;
        }

        // Identify the first token of `line` (leading spaces skipped). Dispatch
        // is an EXACT match (DOS-like: "HI" is not "HISTORY"); prefix matching
        // is only for completion. Returns a dispatch id (>0), NONE (0) for a
        // known-but-unimplemented command, or -1 for an unknown one.
        public static int Identify(char* line, int len)
        {
            int s = 0;
            while (s < len && line[s] == ' ') s++;
            int end = s;
            while (end < len && line[end] != ' ') end++;
            int tl = end - s;
            for (int i = 0; i < _t.n; i++)
            {
                if (_t.lens[i] == tl && NameMatches(i, line + s, tl)) return _t.ids[i];
            }
            return -1;
        }

        // Case-insensitive match of the first `tl` chars against name `i`.
        public static bool NameMatches(int i, char* tok, int tl)
        {
            int nl = _t.lens[i];
            if (nl < tl) return false;
            fixed (char* b = _t.names)
            {
                for (int k = 0; k < tl; k++)
                    if (Upper(tok[k]) != Upper(b[i * NameCap + k])) return false;
            }
            return true;
        }

        public static int NameLen(int i) => _t.lens[i];

        public static void NameAt(int i, char* dst)
        {
            fixed (char* b = _t.names)
            {
                for (int k = 0; k < _t.lens[i]; k++) dst[k] = b[i * NameCap + k];
            }
        }

        public static char Upper(char c) => (c >= 'a' && c <= 'z') ? (char)(c - 32) : c;
    }
}