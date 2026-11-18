using OsbKT;

namespace Osb.Shell.Userspace
{
    // Slice 2 line editor (S2.1/S2.2 / 2.1 prelude). Reads one line from GETCHAR
    // with full cursor editing over the kernel console:
    //   * printable insert at the caret, Backspace/Delete, Left/Right/Home/End
    //   * Up/Down walk History (draft preserved; Ctrl+C discards the edit)
    //   * Tab runs Completion on the token at the caret
    //   * Enter returns the line; Ctrl+C aborts (-1)
    // Redraw returns to the line start with "\b" x screenCur (the kernel "\b" is
    // wrap-aware), reprints, pads over a longer previous line, then "\b" back to
    // the caret; correct across the 80-column wrap and screen scroll. The screen
    // cursor offset (screenCur) is tracked explicitly because a completion can
    // change the buffer caret without moving the cursor. Zero GC allocations.
    internal static unsafe class LineEditor
    {
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

            int len = 0;             // buffer length
            int cur = 0;             // caret index in [0..len]
            int drawn = 0;           // line chars currently on screen
            int screenCur = promptLen;   // cursor offset from the prompt start

            for (;;)
            {
                int ch = Syscall.GetChar();
                if (ch == -1) { Syscall.Sleep(1); continue; }
                if (ch == -2) { Abort(); return -1; }

                if (ch == '\r' || ch == '\n')
                {
                    if (ch == '\n' && _skipLf) { _skipLf = false; continue; }
                    Syscall.PutChar('\r'); Syscall.PutChar('\n');
                    buf[len] = '\0';
                    _skipLf = ch == '\r';
                    History.NavReset();
                    return len;
                }
                _skipLf = false;

                if (ch == 0x1B) { Escape(buf, cap, ref len, ref cur, prompt, promptLen, ref drawn, ref screenCur); continue; }

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
                            drawn = len; screenCur = promptLen + cur;
                        }
                        else Redraw(prompt, promptLen, buf, len, cur, ref drawn, ref screenCur);
                    }
                    continue;
                }

                if (ch == '\t')
                {
                    bool listed = Completion.Complete(buf, ref len, ref cur, cap, out bool appended, out bool changed);
                    if (listed) Reprint(prompt, promptLen, buf, len, cur, ref drawn, ref screenCur);
                    else if (appended && cur == len && drawn <= len)
                    {
                        // The completion only appended characters at the caret:
                        // echo just the new ones (the typed prefix is unchanged
                        // on screen, so no repositioning is needed).
                        for (int k = drawn; k < len; k++) Syscall.PutChar(buf[k]);
                        drawn = len; screenCur = promptLen + cur;
                    }
                    else if (changed)
                        Redraw(prompt, promptLen, buf, len, cur, ref drawn, ref screenCur);
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
                            // Typing at the end: just echo the char (no redraw).
                            Syscall.PutChar((char)ch);
                            drawn = len; screenCur = promptLen + cur;
                        }
                        else Redraw(prompt, promptLen, buf, len, cur, ref drawn, ref screenCur);
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
        // A lone ESC costs at most a few ticks: the rest of a real sequence is
        // pushed atomically by the kernel, so a short wait is enough.
        private static void Escape(char* buf, int cap, ref int len, ref int cur,
                                   char* prompt, int promptLen, ref int drawn, ref int screenCur)
        {
            int c1 = NextByte(5);
            if (c1 != '[') return;                 // not a CSI we understand
            int c2 = NextByte(5);
            if (c2 == '3')                         // ESC [ 3 ~
            {
                int c3 = NextByte(5);
                if (c3 == '~' && cur < len)
                {
                    for (int k = cur; k < len - 1; k++) buf[k] = buf[k + 1];
                    len--;
                    Redraw(prompt, promptLen, buf, len, cur, ref drawn, ref screenCur);
                }
                return;
            }
            switch (c2)
            {
                case 'A': if (History.Prev(buf, ref len)) { cur = len; Redraw(prompt, promptLen, buf, len, cur, ref drawn, ref screenCur); } return;
                case 'B': if (History.Next(buf, ref len)) { cur = len; Redraw(prompt, promptLen, buf, len, cur, ref drawn, ref screenCur); } return;
                case 'C': if (cur < len) { Syscall.PutChar(buf[cur]); cur++; screenCur++; } return;
                case 'D': if (cur > 0) { Syscall.PutChar('\b'); cur--; screenCur--; } return;
                case 'H': while (cur > 0) { Syscall.PutChar('\b'); cur--; screenCur--; } return;
                case 'F': while (cur < len) { Syscall.PutChar(buf[cur]); cur++; screenCur++; } return;
                default: return;
            }
        }

        // Redraw the prompt + line and place the caret at `cur`. Returns to the
        // line start with "\b" x screenCur: the kernel "\b" is wrap-aware
        // (column 0 steps to the previous row's last column), so this is correct
        // when prompt+line exceeds 80 columns and the screen scrolls (the prompt
        // can only scroll off with a line longer than the 128-char cap, which
        // cannot happen). NOTE: on a real serial terminal the wrap behavior of
        // "\b" is terminal dependent; this is correct for the kernel VGA console
        // and the QEMU serial capture.
        private static void Redraw(char* prompt, int promptLen, char* buf, int len, int cur,
                                   ref int drawn, ref int screenCur)
        {
            for (int k = 0; k < screenCur; k++) Syscall.PutChar('\b');
            if (promptLen > 0) Syscall.Print(prompt, promptLen);
            if (len > 0) Syscall.Print(buf, len);
            int displayed = len > drawn ? len : drawn;
            for (int k = len; k < displayed; k++) Syscall.PutChar(' ');
            for (int k = 0; k < displayed - cur; k++) Syscall.PutChar('\b');
            drawn = len;
            screenCur = promptLen + cur;
        }

        // Reprint the prompt + line at the current cursor (assumed column 0,
        // after a completion listing pushed the prompt to a fresh line) and
        // place the caret at `cur`. No "\b" repositioning: the cursor is already
        // at the start of the fresh line.
        private static void Reprint(char* prompt, int promptLen, char* buf, int len, int cur,
                                    ref int drawn, ref int screenCur)
        {
            if (promptLen > 0) Syscall.Print(prompt, promptLen);
            if (len > 0) Syscall.Print(buf, len);
            for (int k = 0; k < len - cur; k++) Syscall.PutChar('\b');
            drawn = len;
            screenCur = promptLen + cur;
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