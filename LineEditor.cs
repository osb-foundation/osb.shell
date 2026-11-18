using OsbKT;

namespace Osb.Shell.Userspace
{
    // Slice 2 line editor (S2.1/S2.2). Reads one line from GETCHAR with full
    // cursor editing over the kernel console:
    //   * printable insert at the caret, Backspace/Delete, Left/Right/Home/End
    //   * Up/Down walk History (draft preserved; Ctrl+C discards the edit)
    //   * Tab runs Completion on the token at the caret
    //   * Enter returns the line; Ctrl+C aborts (-1)
    // Redraw is "\r" + reprint prompt + reprint line + pad + "\b"*(len-cur);
    // the wrap-aware "\b" added to the kernel console (1c.1) is what makes the
    // cursor positioning correct. Zero GC allocations (stack buffers only).
    internal static unsafe class LineEditor
    {
        private const int NameCap = 32;

        // Set when the previous line ended on a CR, so an LF that immediately
        // follows a CR (CRLF) is swallowed instead of read as a second Enter.
        private static bool _skipLf;

        // Read one line into buf[0..cap-1]. `prompt` is echoed first (promptLen
        // chars). `echo` false = password mode (no echo, no editing). `hist`
        // enables Up/Down history navigation. Returns the length, or -1 if
        // Ctrl+C aborted.
        public static int Read(char* prompt, int promptLen, char* buf, int cap, bool echo, bool hist)
        {
            if (promptLen > 0) Syscall.Print(prompt, promptLen);

            if (!echo) return ReadNoEcho(buf, cap);

            int len = 0;
            int cur = 0;          // caret index in [0..len]
            int lastDrawn = 0;    // chars of the line currently on screen

            for (;;)
            {
                int ch = Syscall.GetChar();
                if (ch == -1) { Syscall.Sleep(1); continue; }
                if (ch == -2) { Abort(); return -1; }

                if (ch == '\r')
                {
                    Syscall.PutChar('\r'); Syscall.PutChar('\n');
                    buf[len] = '\0';
                    _skipLf = true;
                    History.NavReset();
                    return len;
                }
                if (ch == '\n')
                {
                    if (_skipLf) { _skipLf = false; continue; }
                    Syscall.PutChar('\r'); Syscall.PutChar('\n');
                    buf[len] = '\0';
                    History.NavReset();
                    return len;
                }
                _skipLf = false;

                if (ch == 0x1B) { lastDrawn = Escape(buf, cap, ref len, ref cur, prompt, promptLen, lastDrawn); continue; }

                if (ch == '\b' || ch == 0x7F)
                {
                    if (cur > 0)
                    {
                        bool atEnd = (cur == len);
                        for (int k = cur - 1; k < len - 1; k++) buf[k] = buf[k + 1];
                        len--; cur--;
                        if (atEnd)
                        {
                            // Backspace at the end: the classic erase sequence,
                            // matching the 1b line reader (golden 25-getchar).
                            Syscall.PutChar('\b'); Syscall.PutChar(' '); Syscall.PutChar('\b');
                            lastDrawn = len;
                        }
                        else lastDrawn = Redraw(prompt, promptLen, buf, len, cur, lastDrawn);
                    }
                    continue;
                }

                if (ch == '\t')
                {
                    int oldLen = len;
                    Completion.Complete(buf, ref len, ref cur, cap);
                    if (len != oldLen) lastDrawn = Redraw(prompt, promptLen, buf, len, cur, lastDrawn);
                    continue;
                }

                if (ch >= ' ' && ch < 127)
                {
                    if (len < cap - 1)
                    {
                        bool atEnd = (cur == len);
                        for (int k = len; k > cur; k--) buf[k] = buf[k - 1];
                        buf[cur] = (char)ch;
                        len++; cur++;
                        if (atEnd)
                        {
                            // Typing at the end: just echo the char (no redraw),
                            // so a serial transcript stays one char per keystroke.
                            Syscall.PutChar((char)ch);
                            lastDrawn = len;
                        }
                        else lastDrawn = Redraw(prompt, promptLen, buf, len, cur, lastDrawn);
                    }
                    continue;
                }
            }
        }

        // Password mode: no echo, no editing, no history. Backspace trims the
        // tail; everything else printable appends.
        private static int ReadNoEcho(char* buf, int cap)
        {
            int len = 0;
            for (;;)
            {
                int ch = Syscall.GetChar();
                if (ch == -1) { Syscall.Sleep(1); continue; }
                if (ch == -2) { Abort(); return -1; }
                if (ch == '\r' || ch == '\n')
                {
                    if (ch == '\n' && _skipLf) { _skipLf = false; continue; }
                    Syscall.PutChar('\n');
                    buf[len] = '\0';
                    _skipLf = ch == '\r';
                    return len;
                }
                _skipLf = false;
                if (ch == '\b' || ch == 0x7F) { if (len > 0) len--; continue; }
                if (ch >= ' ' && ch < 127 && len < cap - 1) buf[len++] = (char)ch;
            }
        }

        // Parse an ANSI CSI sequence (ESC already consumed). Handles the arrows,
        // Home/End and Delete the kernel synthesizes for PS/2 (ConsoleInput).
        private static int Escape(char* buf, int cap, ref int len, ref int cur,
                                  char* prompt, int promptLen, int lastDrawn)
        {
            int c1 = NextByte(100);
            if (c1 != '[') return lastDrawn;      // not a CSI we understand
            int c2 = NextByte(100);
            if (c2 == '3')                        // ESC [ 3 ~
            {
                int c3 = NextByte(100);
                if (c3 == '~' && cur < len)
                {
                    for (int k = cur; k < len - 1; k++) buf[k] = buf[k + 1];
                    len--;
                    return Redraw(prompt, promptLen, buf, len, cur, lastDrawn);
                }
                return lastDrawn;
            }
            switch (c2)
            {
                case 'A': if (History.Prev(buf, ref len)) cur = len; return Redraw(prompt, promptLen, buf, len, cur, lastDrawn);
                case 'B': if (History.Next(buf, ref len)) cur = len; return Redraw(prompt, promptLen, buf, len, cur, lastDrawn);
                case 'C': if (cur < len) { Syscall.PutChar(buf[cur]); cur++; } return lastDrawn;
                case 'D': if (cur > 0) { Syscall.PutChar('\b'); cur--; } return lastDrawn;
                case 'H': while (cur > 0) { Syscall.PutChar('\b'); cur--; } return lastDrawn;
                case 'F': while (cur < len) { Syscall.PutChar(buf[cur]); cur++; } return lastDrawn;
                default: return lastDrawn;
            }
        }

        // Redraw the prompt + line and place the caret at `cur`. Returns the new
        // lastDrawn (== len). "\r" to column 0, reprint, pad over any leftover
        // chars, then "\b" back to the caret.
        private static int Redraw(char* prompt, int promptLen, char* buf, int len, int cur, int lastDrawn)
        {
            Syscall.PutChar('\r');
            Syscall.Print(prompt, promptLen);
            Syscall.Print(buf, len);
            int displayed = len > lastDrawn ? len : lastDrawn;
            for (int k = len; k < lastDrawn; k++) Syscall.PutChar(' ');
            for (int k = 0; k < displayed - cur; k++) Syscall.PutChar('\b');
            return len;
        }

        private static void Abort()
        {
            Syscall.PutChar('\r'); Syscall.PutChar('\n');
            History.NavReset();
        }

        // Next cooked byte, waiting up to `tries` ticks (bytes for a sequence are
        // pushed atomically, so this normally returns on the first try).
        private static int NextByte(int tries)
        {
            for (int i = 0; i < tries; i++)
            {
                int c = Syscall.GetChar();
                if (c != -1) return c;
                Syscall.Sleep(1);
            }
            return -1;
        }
    }
}