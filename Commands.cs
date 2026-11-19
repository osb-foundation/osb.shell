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
        public const int SET = 8;
        public const int DIR = 9;
        public const int CD = 10;
        public const int PWD = 11;
        public const int TYPE = 12;
        public const int SIZE = 13;
        public const int MD = 14;
        public const int RD = 15;
        public const int DEL = 16;
        public const int REN = 17;
        public const int COPY = 18;
        public const int LN = 19;
        public const int TRASH = 20;
        public const int VER = 21;
        public const int HELP = 23;
        public const int CLS = 24;
        public const int MEM = 25;
        public const int REBOOT = 29;
        public const int TIME = 30;
        public const int DATE = 31;
        public const int TICKS = 32;
        public const int SLEEP = 33;
        public const int TASK = 37;
        public const int PS = 37;
        public const int KILL = 38;
        public const int WAIT = 39;
        public const int TAB = 40;
        public const int SPAWN = 41;
        public const int USER = 46;
        public const int CHOWN = 47;
        public const int CHMOD = 48;

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
            // implemented commands have real ids; the rest complete but are not run yet.
            Add("VER", VER); Add("HELP", HELP); Add("CLS", CLS);
            Add("CLEAR", CLS); Add("MEM", MEM);
            Add("EXIT", EXIT); Add("REBOOT", REBOOT);
            Add("TIME", TIME); Add("DATE", DATE); Add("TICKS", TICKS); Add("SLEEP", SLEEP);
            Add("ECHO", ECHO); Add("SET", SET); Add("DIR", DIR); Add("CD", CD);
            Add("PWD", PWD); Add("TYPE", TYPE); Add("DEL", DEL);
            Add("MD", MD); Add("MKDIR", MD); Add("RD", RD);
            Add("RMDIR", RD); Add("REN", REN); Add("MOVE", REN);
            Add("COPY", COPY); Add("CP", COPY); Add("SIZE", SIZE);
            Add("LN", LN);
            Add("TASK", TASK); Add("PS", PS); Add("KILL", KILL); Add("WAIT", WAIT);
            Add("TAB", TAB); Add("SPAWN", SPAWN); Add("TRASH", TRASH);
            Add("HISTORY", HISTORY); Add("USER", USER);
            Add("LOGIN", LOGIN); Add("LOGOUT", LOGOUT); Add("WHOAMI", WHOAMI);
            Add("CHOWN", CHOWN); Add("CHMOD", CHMOD); Add("SUDO", SUDO);
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