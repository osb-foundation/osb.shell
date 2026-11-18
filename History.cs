using OsbKT;

namespace Osb.Shell.Userspace
{
    // Slice 2 history: a fixed-size ring of submitted command lines, ported
    // from the legacy in-kernel History (Osb.Kernel.Shell.History) minus the
    // Vga/ListCommand output (that becomes a shell command). No managed arrays
    // (the freestanding runtime has none): the ring lives in a fixed struct.
    //
    // Entry numbers are the monotonic push order (1-based), so `HISTORY` prints
    // stable numbers and `(:N)` re-runs entry N. Up/Down navigation keeps a
    // draft of the line being edited so Down can restore it. Empty lines and
    // consecutive duplicates are skipped (legacy parity).
    internal static unsafe class History
    {
        public const int MaxEntries = 256;    // ring slots (matches Layout)
        public const int LineCap = 128;       // chars per entry

        private struct Ring
        {
            public fixed char buf[MaxEntries * LineCap];
            public fixed int lens[MaxEntries];
            public fixed char draft[LineCap];
            public int pushed;    // total entries ever pushed (entry numbers)
            public int nav;       // visible-entry cursor, or -1 = idle
            public int draftLen;
        }

        private static Ring _r;

        // Entries currently visible in the ring.
        public static int Count => _r.pushed < MaxEntries ? _r.pushed : MaxEntries;

        // Records a submitted line. Empty lines and consecutive duplicates are
        // skipped; navigation resets to the draft.
        public static void Push(char* line, int len)
        {
            if (len <= 0) return;
            if (len > LineCap - 1) len = LineCap - 1;

            if (Count > 0)
            {
                int last = Count - 1;
                if (_r.lens[SlotOf(last)] == len)
                {
                    bool same = true;
                    for (int k = 0; k < len; k++)
                        if (EntryChar(last, k) != line[k]) { same = false; break; }
                    if (same) { _r.nav = -1; return; }   // consecutive duplicate
                }
            }

            int slot = _r.pushed % MaxEntries;
            fixed (char* b = _r.buf)
            {
                for (int k = 0; k < len; k++) b[slot * LineCap + k] = line[k];
            }
            _r.lens[slot] = len;
            _r.pushed++;
            _r.nav = -1;
        }

        // Copies entry number `num` (1-based push order) into `dst`.
        public static bool TryRecall(int num, char* dst, out int len)
        {
            len = 0;
            if (num <= 0 || Count == 0) return false;
            int oldest = _r.pushed - Count + 1;
            if (num < oldest || num > _r.pushed) return false;
            int idx = num - oldest;
            int l = _r.lens[SlotOf(idx)];
            if (l > LineCap - 1) l = LineCap - 1;
            int slot = SlotOf(idx);
            fixed (char* b = _r.buf)
            {
                for (int k = 0; k < l; k++) dst[k] = b[slot * LineCap + k];
            }
            len = l;
            return true;
        }

        // Copies visible entry `idx` (0 = oldest visible) into `dst`.
        public static bool CopyVisible(int idx, char* dst, out int len)
        {
            len = 0;
            if (idx < 0 || idx >= Count) return false;
            int l = _r.lens[SlotOf(idx)];
            if (l > LineCap - 1) l = LineCap - 1;
            int slot = SlotOf(idx);
            fixed (char* b = _r.buf)
            {
                for (int k = 0; k < l; k++) dst[k] = b[slot * LineCap + k];
            }
            len = l;
            return true;
        }

        // Number (1-based push order) of visible entry `idx`, for HISTORY.
        public static int NumberOf(int idx)
        {
            int oldest = _r.pushed - Count + 1;
            return oldest + idx;
        }

        // True when the line looks like a "(:N)" recall expression.
        public static bool LooksLikeRecall(char* line, int len)
        {
            return len >= 4 && line[0] == '(' && line[1] == ':' && line[len - 1] == ')';
        }

        // Recognizes "(:N)" and copies entry N into outLine.
        public static bool ParseRecall(char* line, int len, char* outLine, out int outLen)
        {
            outLen = 0;
            if (len < 4 || line[0] != '(' || line[1] != ':') return false;
            int n = 0;
            int i = 2;
            bool digits = false;
            while (i < len && line[i] >= '0' && line[i] <= '9')
            {
                n = n * 10 + (line[i] - '0');
                i++;
                digits = true;
            }
            if (!digits || i != len - 1 || line[i] != ')') return false;
            return TryRecall(n, outLine, out outLen);
        }

        // Drops the navigation cursor (after running a line or Ctrl+C).
        public static void NavReset() => _r.nav = -1;

        // One older entry into the edit buffer (Up). Returns false when empty.
        public static bool Prev(char* line, ref int len)
        {
            int count = Count;
            if (count == 0) return false;
            if (_r.nav < 0)
            {
                int n = len > LineCap - 1 ? LineCap - 1 : len;
                fixed (char* d = _r.draft)
                {
                    for (int k = 0; k < n; k++) d[k] = line[k];
                }
                _r.draftLen = n;
                _r.nav = count - 1;
            }
            else if (_r.nav > 0)
            {
                _r.nav--;
            }
            CopyVisible(_r.nav, line, out len);
            return true;
        }

        // One newer entry (Down); past the newest entry the saved draft returns.
        public static bool Next(char* line, ref int len)
        {
            if (_r.nav < 0) return false;
            int count = Count;
            if (_r.nav < count - 1)
            {
                _r.nav++;
                CopyVisible(_r.nav, line, out len);
            }
            else
            {
                _r.nav = -1;
                int n = _r.draftLen;
                fixed (char* d = _r.draft)
                {
                    for (int k = 0; k < n; k++) line[k] = d[k];
                }
                len = n;
            }
            return true;
        }

        // ---- ring index helpers ----

        private static int SlotOf(int idx)
        {
            int oldestSlot = (_r.pushed - Count) % MaxEntries;
            return (oldestSlot + idx) % MaxEntries;
        }

        private static char EntryChar(int idx, int pos)
        {
            int slot = SlotOf(idx);
            return _r.buf[slot * LineCap + pos];
        }
    }
}