using OsbKT;

namespace Osb.Shell.Userspace
{
    // Slice 2 TAB completion (S2.3 / 2.1 prelude). Completes the token at the
    // caret:
    //   * first token          -> a command name (Commands table)
    //   * token with '/' lead  -> flags (not yet: no flags in this shell)
    //   * otherwise            -> a file/directory name, resolved via 20h
    //                             RESOLVE + 21h DIRNEXT against A:
    // A single match replaces the typed token with the real (canonical-case)
    // name (the FS is case-sensitive, so "t<TAB>" against TRASH must end up as
    // "TRASH" in the buffer, never "tRASH"), plus a '\' for a directory. When
    // the typed prefix is already canonical the completion only ECHOES the new
    // characters ("HI<TAB>" prints "STORY", §1.4 "extended in place"); when
    // the case differs the caller redraws the line with the canonical name.
    // Command names are likewise canonical. Several matches are LISTED (two
    // spaces apart) and the token is extended to their longest common prefix.
    // No match does nothing. Zero GC allocations (stack buffers only).
    internal static unsafe class Completion
    {
        private const int MaxMatches = 64;
        private const int NameCap = 32;
        private const int PathCap = 200;

        // Returns true when matches were LISTED (the caller must Reprint the
        // prompt + line from column 0); false for a silent completion. `appended`
        // is true for a silent single match that only EXTENDED the line at the
        // caret with the canonical spelling (caret at the end, typed prefix
        // already canonical): the caller can echo just the new characters, which
        // is the "extended in place" behavior of §1.4 (no redraw). `changed` is
        // true when a silent completion modified the line in any other way (a
        // canonical-case fix, an extension mid-line, a directory separator) and
        // the caller must redraw.
        public static bool Complete(char* line, ref int len, ref int cur, int cap, out bool appended, out bool changed)
        {
            appended = false;
            changed = false;
            int ts = 0;   // token start = just after the last space before the caret
            for (int k = 0; k < cur; k++) if (line[k] == ' ') ts = k + 1;
            int tl = cur - ts;

            if (ts == 0) return CompleteCommand(line, ref len, ref cur, ts, tl, cap, ref appended, ref changed);
            if (tl > 0 && line[ts] == '/') return false;   // flags: none yet
            return CompleteFile(line, ref len, ref cur, ts, tl, cap, ref appended, ref changed);
        }

        // ---- command names ----

        private static bool CompleteCommand(char* line, ref int len, ref int cur, int ts, int tl, int cap, ref bool appended, ref bool changed)
        {
            if (tl <= 0) return false;
            char* first = stackalloc char[NameCap];
            char* nm = stackalloc char[NameCap];
            int firstLen = 0;
            int count = 0;
            for (int i = 0; i < Commands.Count; i++)
            {
                if (!Commands.NameMatches(i, line + ts, tl)) continue;
                if (count == 0) { firstLen = Commands.NameLen(i); Commands.NameAt(i, first); }
                count++;
            }
            if (count == 0) return false;
            if (count == 1)
            {
                appended = Appended(line, ts, tl, cur, len, first, firstLen);
                changed = ReplaceToken(line, ref len, ref cur, ts, tl, first, firstLen, cap);
                return false;
            }
            // several: list them (canonical names, two spaces apart) then
            // extend the token to the longest common prefix (canonical case).
            int lcpLen = firstLen;
            Syscall.PutChar('\n');
            bool firstOut = true;
            for (int i = 0; i < Commands.Count; i++)
            {
                if (!Commands.NameMatches(i, line + ts, tl)) continue;
                Commands.NameAt(i, nm);
                int nl = Commands.NameLen(i);
                if (!firstOut) Syscall.PrintString("  ");
                firstOut = false;
                Syscall.Print(nm, nl);
                int c = 0;
                while (c < lcpLen && c < nl && Commands.Upper(first[c]) == Commands.Upper(nm[c])) c++;
                if (c < lcpLen) lcpLen = c;
            }
            Syscall.PutChar('\n');
            if (lcpLen > 0) changed = ReplaceToken(line, ref len, ref cur, ts, tl, first, lcpLen, cap);
            return true;
        }

        // ---- file and directory names ----

        private static bool CompleteFile(char* line, ref int len, ref int cur, int ts, int tl, int cap, ref bool appended, ref bool changed)
        {
            int split = -1;   // last separator in the token
            for (int k = 0; k < tl; k++)
                if (line[ts + k] == '\\' || line[ts + k] == '/') split = k;
            char* namePrefix = line + ts + split + 1;
            int npLen = tl - split - 1;
            if (npLen > NameCap - 1) return false;

            // directory to list: the prefix, or the current directory
            char* dirPath = stackalloc char[PathCap];
            int dlen = 0;
            if (split < 0)
            {
                byte* cwd = stackalloc byte[PathCap];
                int cl = Syscall.GetCwd(cwd, PathCap);
                if (cl < 0) return false;
                for (int k = 0; k < cl; k++) dirPath[k] = (char)cwd[k];
                dlen = cl;
            }
            else if (split == 0)
            {
                dirPath[0] = '\\';   // root-anchored token: "\name"
                dlen = 1;
            }
            else
            {
                for (int k = 0; k < split; k++) dirPath[k] = line[ts + k];
                dlen = split;
            }

            uint dirIno;
            if (!ResolveDir(dirPath, dlen, out dirIno)) return false;

            char* names = stackalloc char[MaxMatches * NameCap];
            int* nlens = stackalloc int[MaxMatches];
            byte* types = stackalloc byte[MaxMatches];
            char* first = stackalloc char[NameCap];
            char* nm = stackalloc char[NameCap];
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
            if (count == 0) return false;

            if (count == 1)
            {
                int ts2 = ts + split + 1;   // the name inside the token (dir kept)
                appended = Appended(line, ts2, npLen, cur, len, first, firstLen);
                changed = ReplaceToken(line, ref len, ref cur, ts2, npLen, first, firstLen, cap);
                if (types[0] == 2 && cur < cap - 1)   // directory: add a separator
                {
                    for (int k = len; k > cur; k--) line[k] = line[k - 1];
                    line[cur] = '\\';
                    len++; cur++; changed = true;
                }
                return false;
            }

            // several: list them, then extend to the longest common prefix.
            int lcpLen = firstLen;
            Syscall.PutChar('\n');
            bool firstOut = true;
            for (int c = 0; c < count; c++)
            {
                for (int k = 0; k < nlens[c]; k++) nm[k] = names[c * NameCap + k];
                int nl = nlens[c];
                if (!firstOut) Syscall.PrintString("  ");
                firstOut = false;
                Syscall.Print(nm, nl);
                int m = 0;
                while (m < lcpLen && m < nl && Upper(first[m]) == Upper(nm[m])) m++;
                if (m < lcpLen) lcpLen = m;
            }
            Syscall.PutChar('\n');
            if (lcpLen > 0)
            {
                int ts2 = ts + split + 1;   // the name inside the token (dir kept)
                changed = ReplaceToken(line, ref len, ref cur, ts2, npLen, first, lcpLen, cap);
            }
            return true;
        }

        // Resolve a directory path (char*) to its inode via the osbkt RESOLVE
        // wrapper (A6: the shell never calls Syscall.Raw).
        private static bool ResolveDir(char* path, int plen, out uint ino)
        {
            ino = 0;
            uint rino; ushort rmode; ushort ruid; ulong rsize;
            if (!Syscall.ResolvePath(path, plen, out rino, out rmode, out ruid, out rsize)) return false;
            if ((rmode & 0xF000) != 0x4000) return false;   // directory
            ino = rino;
            return true;
        }

        // Replace the token line[ts..ts+tl) with repl[0..replLen) (canonical
        // case), shifting the rest right. The typed token is a case-insensitive
        // prefix, so replLen >= tl. Adjusts len and cur (caret ends at
        // ts + replLen).
        private static bool ReplaceToken(char* line, ref int len, ref int cur, int ts, int tl,
                                         char* repl, int replLen, int cap)
        {
            if (replLen < tl) { replLen = tl; }   // safety: never shrink below the token
            int delta = replLen - tl;
            bool changed = delta != 0;
            if (delta > 0)
            {
                int room = cap - 1 - len;
                if (delta > room) { replLen -= (delta - room); delta = replLen - tl; }
                if (delta > 0)
                    for (int k = len - 1; k >= ts + tl; k--) line[k + delta] = line[k];
            }
            for (int k = 0; k < replLen; k++)
            {
                if (line[ts + k] != repl[k]) changed = true;
                line[ts + k] = repl[k];
            }
            len += delta;
            cur = ts + replLen;
            return changed;
        }

        // True when the single match only EXTENDS the line at the caret: the
        // caret is at the end of the line and the typed prefix is already the
        // canonical spelling, so echoing the new characters is enough (no
        // redraw, §1.4 "extended in place"). `ts`/`tl` delimit the part of the
        // token that the match replaces (the name for file completion).
        private static bool Appended(char* line, int ts, int tl, int cur, int len, char* repl, int replLen)
        {
            if (cur != len || replLen < tl) return false;
            for (int k = 0; k < tl; k++) if (line[ts + k] != repl[k]) return false;
            return true;
        }

        private static char Upper(char c) => (c >= 'a' && c <= 'z') ? (char)(c - 32) : c;
    }
}