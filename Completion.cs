using OsbKT;

namespace Osb.Shell.Userspace
{
    // Slice 2 TAB completion (S2.3). Completes the token at the caret:
    //   * first token          -> a command name (Commands table)
    //   * token with '/' lead  -> flags (not yet: no flags in this shell)
    //   * otherwise            -> a file/directory name, resolved via 20h
    //                             RESOLVE + 21h DIRNEXT against A:
    // A single match appends the missing chars (plus '\' for a directory);
    // several matches extend the token to their longest common prefix; no
    // match does nothing. Zero GC allocations (stack buffers only).
    internal static unsafe class Completion
    {
        private const int MaxMatches = 64;
        private const int NameCap = 32;
        private const int PathCap = 200;

        public static void Complete(char* line, ref int len, ref int cur, int cap)
        {
            int ts = 0;   // token start = just after the last space before the caret
            for (int k = 0; k < cur; k++) if (line[k] == ' ') ts = k + 1;
            int tl = cur - ts;

            if (ts == 0) { CompleteCommand(line, ref len, ref cur, ts, tl, cap); return; }
            if (tl > 0 && line[ts] == '/') return;   // flags: none yet
            CompleteFile(line, ref len, ref cur, ts, tl, cap);
        }

        // ---- command names ----

        private static void CompleteCommand(char* line, ref int len, ref int cur, int ts, int tl, int cap)
        {
            if (tl <= 0) return;
            char* first = stackalloc char[NameCap];
            int firstLen = 0;
            int count = 0;
            for (int i = 0; i < Commands.Count; i++)
            {
                if (!Commands.NameMatches(i, line + ts, tl)) continue;
                if (count == 0) { firstLen = Commands.NameLen(i); Commands.NameAt(i, first); }
                count++;
            }
            if (count == 0) return;
            if (count == 1)
            {
                AppendSuffix(line, ref len, ref cur, ts, tl, first, tl, firstLen - tl, cap);
                return;
            }
            // several: extend to the longest common prefix
            int lcpLen = firstLen;
            char* nm = stackalloc char[NameCap];
            for (int i = 0; i < Commands.Count; i++)
            {
                if (!Commands.NameMatches(i, line + ts, tl)) continue;
                Commands.NameAt(i, nm);
                int nl = Commands.NameLen(i);
                int c = 0;
                while (c < lcpLen && c < nl && Commands.Upper(first[c]) == Commands.Upper(nm[c])) c++;
                lcpLen = c;
            }
            if (lcpLen > tl) AppendSuffix(line, ref len, ref cur, ts, tl, first, tl, lcpLen - tl, cap);
        }

        // ---- file and directory names ----

        private static void CompleteFile(char* line, ref int len, ref int cur, int ts, int tl, int cap)
        {
            int split = -1;   // last separator in the token
            for (int k = 0; k < tl; k++)
                if (line[ts + k] == '\\' || line[ts + k] == '/') split = k;
            char* namePrefix = line + ts + split + 1;
            int npLen = tl - split - 1;
            if (npLen > NameCap - 1) return;

            // directory to list: the prefix, or the current directory
            char* dirPath = stackalloc char[PathCap];
            int dlen = 0;
            if (split < 0)
            {
                byte* cwd = stackalloc byte[PathCap];
                int cl = Syscall.GetCwd(cwd, PathCap);
                if (cl < 0) return;
                for (int k = 0; k < cl; k++) dirPath[k] = (char)cwd[k];
                dlen = cl;
            }
            else
            {
                for (int k = 0; k < split; k++) dirPath[k] = line[ts + k];
                dlen = split;
            }

            uint dirIno;
            if (!ResolveDir(dirPath, dlen, out dirIno)) return;

            char* names = stackalloc char[MaxMatches * NameCap];
            int* nlens = stackalloc int[MaxMatches];
            byte* types = stackalloc byte[MaxMatches];
            char* first = stackalloc char[NameCap];
            int firstLen = 0;
            int count = 0;
            byte* ent = stackalloc byte[240];
            for (int index = 0; count < MaxMatches; index++)
            {
                if (!Syscall.DirNext(dirIno, index, ent, 240)) break;
                uint iino = (uint)(ent[0] | (ent[1] << 8) | (ent[2] << 16) | (ent[3] << 24));
                if (iino == 0) continue;
                byte nlen = ent[5];
                if (nlen == 0 || nlen >= NameCap || nlen < npLen) continue;
                bool match = true;
                for (int k = 0; k < npLen; k++)
                {
                    char a = Upper(namePrefix[k]);
                    char b = Upper((char)ent[6 + k]);
                    if (a != b) { match = false; break; }
                }
                if (!match) continue;
                for (int k = 0; k < nlen; k++) names[count * NameCap + k] = (char)ent[6 + k];
                nlens[count] = nlen;
                types[count] = ent[4];
                if (count == 0) { firstLen = nlen; for (int k = 0; k < nlen; k++) first[k] = (char)ent[6 + k]; }
                count++;
            }
            if (count == 0) return;

            if (count == 1)
            {
                AppendSuffix(line, ref len, ref cur, ts, tl, first, npLen, firstLen - npLen, cap);
                if (types[0] == 2 && cur < cap - 1)   // directory: add a separator
                {
                    for (int k = len; k > cur; k--) line[k] = line[k - 1];
                    line[cur] = '\\';
                    len++; cur++;
                }
                return;
            }

            // several: longest common prefix of the matched names
            int lcpLen = firstLen;
            for (int c = 0; c < count; c++)
            {
                char* nm = names + c * NameCap;
                int nl = nlens[c];
                int k = 0;
                while (k < lcpLen && k < nl && Upper(first[k]) == Upper(nm[k])) k++;
                if (k < lcpLen) lcpLen = k;
            }
            if (lcpLen > npLen) AppendSuffix(line, ref len, ref cur, ts, tl, first, npLen, lcpLen - npLen, cap);
        }

        // Resolve a directory path (char*) to its inode via the raw 20h RESOLVE.
        private static bool ResolveDir(char* path, int plen, out uint ino)
        {
            ino = 0;
            var pbuf = stackalloc byte[PathCap];
            int n = plen < PathCap ? plen : PathCap;
            for (int k = 0; k < n; k++) pbuf[k] = (byte)path[k];
            byte* outb = stackalloc byte[16];
            if (Syscall.Raw(0x20, 0, (ulong)pbuf, (ulong)n, (ulong)outb, 0) != 1) return false;
            ino = (uint)(outb[0] | (outb[1] << 8) | (outb[2] << 16) | (outb[3] << 24));
            ushort mode = (ushort)(outb[4] | (outb[5] << 8));
            return (mode & 0xF000) == 0x4000;   // directory
        }

        // Insert `suffix[sufStart..sufStart+sufLen)` at the caret (ts+tl),
        // shifting the rest right.
        private static void AppendSuffix(char* line, ref int len, ref int cur, int ts, int tl,
                                         char* suffix, int sufStart, int sufLen, int cap)
        {
            if (sufLen <= 0) return;
            int room = cap - 1 - len;
            if (sufLen > room) sufLen = room;
            if (sufLen <= 0) return;
            int at = ts + tl;
            for (int k = len - 1; k >= at; k--) line[k + sufLen] = line[k];
            for (int k = 0; k < sufLen; k++) line[at + k] = suffix[sufStart + k];
            len += sufLen;
            cur = at + sufLen;
        }

        private static char Upper(char c) => (c >= 'a' && c <= 'z') ? (char)(c - 32) : c;
    }
}