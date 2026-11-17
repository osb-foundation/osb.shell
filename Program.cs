using OsbKT;

namespace Osb.Shell.Userspace
{
    // Phase 1c shell: kernel-owned session identity. Verifies the kernel ABI
    // (>= 4), then runs the legacy login flow (login: / Password:) and a command
    // loop with the `user@OSB cwd>` prompt. Commands: WHOAMI, LOGIN, LOGOUT,
    // SUDO <cmd>, EXIT. No other commands yet. Zero GC allocations (stack
    // buffers only; osbkt is called through its byte*/SpawnReq overloads).
    internal static unsafe class Program
    {
        private const int LineCap = 128;
        private const int NameCap = 32;
        private const int CwdCap = 256;

        // Set when the previous line ended on a CR, so an LF that immediately
        // follows a CR (CRLF) is swallowed instead of read as a second Enter.
        private static bool _skipLf;

        private static void Main()
        {
            if (Syscall.GetABI() < Syscall.AbiVersion)
            {
                Syscall.PrintString("SHELL: kernel ABI too old (need >= ");
                PrintUShort(Syscall.AbiVersion);
                Syscall.PrintString("), halting.\r\n");
                return;
            }

            Login();
            Session();   // returns only on EXIT (which exits the process)
        }

        // Interactive login: "login:" and, for password-protected accounts,
        // "Password:" (no echo). The kernel decides whether a password is
        // needed: we try an empty password first; a -3 means either an unknown
        // user or a user that has a password, so we prompt and retry (the two
        // cases stay indistinguishable to the caller — no user enumeration).
        private static void Login()
        {
            var name = stackalloc char[NameCap];
            var pass = stackalloc char[NameCap];
            var nbuf = stackalloc byte[NameCap];
            var pbuf = stackalloc byte[NameCap];
            for (;;)
            {
                Syscall.PrintString("login: ");
                int nlen = ReadLine(name, NameCap, true);
                if (nlen <= 0) continue;   // aborted or empty: re-prompt
                Narrow(name, nbuf, nlen);  // char (UTF-16) -> byte string

                if (Syscall.Login(nbuf, nlen, null, 0) == 0) { Syscall.PutChar('\n'); return; }

                Syscall.PrintString("Password: ");
                int plen = ReadLine(pass, NameCap, false);
                Syscall.PutChar('\n');   // the extra newline the legacy flow prints
                if (plen < 0) continue;
                Narrow(pass, pbuf, plen);

                if (Syscall.Login(nbuf, nlen, pbuf, plen) == 0) { Syscall.PutChar('\n'); return; }
                Syscall.PrintString("Login incorrect\r\n");
            }
        }

        // Command loop. EXIT exits the process; LOGIN/LOGOUT re-enter Login()
        // in place.
        private static void Session()
        {
            var line = stackalloc char[LineCap];
            for (;;)
            {
                PrintPrompt();
                int len = ReadLine(line, LineCap, true);
                if (len < 0) continue;   // Ctrl+C: re-prompt

                if (IsEq(line, len, "EXIT"))
                {
                    Syscall.PrintString("bye\r\n");
                    Syscall.Exit(0);
                    return;
                }
                if (IsEq(line, len, "WHOAMI")) { DoWhoAmI(); continue; }
                if (IsEq(line, len, "LOGIN")) { Login(); continue; }
                if (IsEq(line, len, "LOGOUT")) { Syscall.Logout(); Login(); continue; }
                if (StartsWith(line, len, "SUDO ")) { DoSudo(line, len); continue; }
                // No other commands yet: silently re-prompt.
            }
        }

        private static void DoWhoAmI()
        {
            var buf = stackalloc byte[NameCap];
            int n = Syscall.WhoAmI(buf, NameCap);
            if (n < 0) n = 0;
            Syscall.Write(buf, n);
            Syscall.PutChar('\r'); Syscall.PutChar('\n');
        }

        // SUDO <cmd>: elevate, run the external program, de-elevates. A
        // non-sudoer gets the legacy message and nothing runs.
        private static void DoSudo(char* line, int len)
        {
            int i = 5;   // skip "SUDO "
            while (i < len && line[i] == ' ') i++;
            if (i >= len) { Syscall.PrintString("Usage: SUDO <command>\r\n"); return; }

            if (Syscall.Sudo() != 0) { Syscall.PrintString("User is not in the sudoers file.\r\n"); return; }

            int pid = SpawnCmd(line + i, len - i);
            if (pid < 0) Syscall.PrintString("SUDO: cannot run command\r\n");
            else Syscall.Wait(pid);
            Syscall.EndSudo();
        }

        // Spawn an external program given the raw command bytes (a DOS path).
        // Builds a SpawnReq on the stack (zero GC allocations).
        private static int SpawnCmd(char* cmd, int clen)
        {
            var path = stackalloc byte[260];
            int n = clen < 260 ? clen : 260;
            for (int k = 0; k < n; k++) path[k] = (byte)cmd[k];
            var req = new Syscall.SpawnReq
            {
                PathPtr = (ulong)path,
                PathLen = (uint)n,
                ArgsPtr = 0,
                ArgsLen = 0,
                StdinHandle = -1,
                StdoutHandle = -1
            };
            return Syscall.SpawnPath(&req);
        }

        private static void PrintPrompt()
        {
            var ubuf = stackalloc byte[NameCap];
            int ulen = Syscall.WhoAmI(ubuf, NameCap);
            if (ulen < 0) ulen = 0;
            Syscall.Write(ubuf, ulen);
            Syscall.PrintString("@OSB ");

            var cbuf = stackalloc byte[CwdCap];
            int clen = Syscall.GetCwd(cbuf, CwdCap);
            if (clen < 0) clen = 0;
            Syscall.Write(cbuf, clen);
            Syscall.PrintString(">");
        }

        // Read one line from GETCHAR. Returns the length (0..cap-1), or -1 if
        // Ctrl+C aborted. Echoes printable chars unless `echo` is false;
        // Backspace erases. On Enter prints CRLF (normalized to LF).
        private static int ReadLine(char* buf, int cap, bool echo)
        {
            int len = 0;
            for (;;)
            {
                int ch = Syscall.GetChar();
                if (ch == -1) { Syscall.Sleep(1); continue; }
                if (ch == -2) { Syscall.PutChar('\r'); Syscall.PutChar('\n'); return -1; }

                if (ch == '\r')
                {
                    Syscall.PutChar('\r'); Syscall.PutChar('\n');
                    buf[len] = '\0';
                    _skipLf = true;
                    return len;
                }
                if (ch == '\n')
                {
                    if (_skipLf) { _skipLf = false; continue; }
                    Syscall.PutChar('\r'); Syscall.PutChar('\n');
                    buf[len] = '\0';
                    return len;
                }
                _skipLf = false;

                if (ch == '\b' || ch == 0x7F)
                {
                    if (len > 0)
                    {
                        len--;
                        if (echo) { Syscall.PutChar('\b'); Syscall.PutChar(' '); Syscall.PutChar('\b'); }
                    }
                    continue;
                }
                if (ch >= ' ' && ch < 127 && len < cap - 1)
                {
                    buf[len++] = (char)ch;
                    if (echo) Syscall.PutChar((char)ch);
                }
            }
        }

        private static bool IsEq(char* buf, int len, string lit)
        {
            if (len != lit.Length) return false;
            fixed (char* p = lit)
            {
                for (int k = 0; k < len; k++)
                    if (Upper(buf[k]) != Upper(p[k])) return false;
            }
            return true;
        }

        private static bool StartsWith(char* buf, int len, string lit)
        {
            if (len < lit.Length) return false;
            fixed (char* p = lit)
            {
                for (int k = 0; k < lit.Length; k++)
                    if (Upper(buf[k]) != Upper(p[k])) return false;
            }
            return true;
        }

        private static char Upper(char c) => (c >= 'a' && c <= 'z') ? (char)(c - 32) : c;

        // char (UTF-16) -> byte string: the byte-string syscalls (LOGIN/WHOAMI/
        // GETCWD) carry ASCII bytes, not UTF-16 chars, so a char* buffer cannot
        // be passed to them directly.
        private static void Narrow(char* src, byte* dst, int n)
        {
            for (int k = 0; k < n; k++) dst[k] = (byte)src[k];
        }

        private static void PrintUShort(ushort v)
        {
            var buf = stackalloc char[8];
            int n = Fmt.U32(buf, v);
            Syscall.Print(buf, n);
        }
    }
}