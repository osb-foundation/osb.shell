using OsbKT;

namespace Osb.Shell.Userspace
{
    // Phase 1b shell: verifies the kernel ABI (>= 3), then runs a minimal
    // zero-allocation line reader on GETCHAR (6Eh). The reader echoes typed
    // characters, handles Backspace and Enter, and aborts the current line on
    // Ctrl+C (GETCHAR returns -2). Only EXIT is a command; anything else is
    // echoed back. Zero GC allocations by design (stack buffers only).
    internal static unsafe class Program
    {
        private const string Banner =
            "OSB Userspace Shell v0.72 (phase 1b)\r\n" +
            "Type EXIT to quit. Ctrl+C aborts a line.\r\n";

        private const string Prompt = "A:\\OSB>";

        // Line capacity (chars, excluding NUL). Kept small and fixed.
        private const int LineCap = 128;

        // Set when the previous line ended on a CR, so an LF that immediately
        // follows a CR (CRLF) is swallowed instead of being read as a second
        // Enter (no extra empty line).
        private static bool _skipLf;

        private static void Main()
        {
            ushort abi = Syscall.GetABI();
            if (abi < Syscall.AbiVersion)
            {
                Syscall.PrintString("SHELL: kernel ABI too old (need >= ");
                PrintUShort(Syscall.AbiVersion);
                Syscall.PrintString("), halting.\r\n");
                return;
            }

            Syscall.PrintString(Banner);

            var line = stackalloc char[LineCap];

            for (;;)
            {
                Syscall.PrintString(Prompt);
                int len = ReadLine(line, LineCap);
                if (len < 0) continue;   // aborted (Ctrl+C): re-prompt

                if (IsExit(line, len))
                {
                    Syscall.PrintString("bye\r\n");
                    Syscall.Exit(0);
                    return;
                }

                // No other commands yet: echo what was typed, then re-prompt.
                Syscall.PrintString("echo: ");
                Syscall.Print(line, len);
                Syscall.PutChar('\r');
                Syscall.PutChar('\n');
            }
        }

        // Read one line from GETCHAR. Returns the length (0..LineCap-1), or -1
        // if Ctrl+C aborted the line. Echoes printable chars; Backspace erases.
        private static int ReadLine(char* buf, int cap)
        {
            int len = 0;
            for (;;)
            {
                int ch = Syscall.GetChar();
                if (ch == -1) { Syscall.Sleep(1); continue; }   // empty: idle (S3), not spin
                if (ch == -2) { Syscall.PutChar('\r'); Syscall.PutChar('\n'); return -1; }

                if (ch == '\r')
                {
                    Syscall.PutChar('\r');
                    Syscall.PutChar('\n');
                    buf[len] = '\0';
                    _skipLf = true;      // swallow an LF that immediately follows
                    return len;
                }

                if (ch == '\n')
                {
                    if (_skipLf) { _skipLf = false; continue; }   // CRLF: LF is not a 2nd Enter
                    Syscall.PutChar('\r');
                    Syscall.PutChar('\n');
                    buf[len] = '\0';
                    return len;
                }

                _skipLf = false;   // any other char ends the "CR then LF" window

                if (ch == '\b' || ch == 0x7F)
                {
                    if (len > 0)
                    {
                        len--;
                        Syscall.PutChar('\b');
                        Syscall.PutChar(' ');
                        Syscall.PutChar('\b');
                    }
                    continue;
                }

                if (ch >= ' ' && ch < 127 && len < cap - 1)
                {
                    buf[len++] = (char)ch;
                    Syscall.PutChar((char)ch);
                }
            }
        }

        private static bool IsExit(char* buf, int len)
        {
            // Case-insensitive compare against "EXIT" (4 chars).
            if (len != 4) return false;
            char e0 = 'E', x0 = 'X', i0 = 'I', t0 = 'T';
            char a = Upper(buf[0]), b = Upper(buf[1]), c = Upper(buf[2]), d = Upper(buf[3]);
            return a == e0 && b == x0 && c == i0 && d == t0;
        }

        private static char Upper(char c)
        {
            return (c >= 'a' && c <= 'z') ? (char)(c - 32) : c;
        }

        private static void PrintUShort(ushort v)
        {
            var buf = stackalloc char[8];
            int n = Fmt.U32(buf, v);
            Syscall.Print(buf, n);
        }
    }
}