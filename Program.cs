using OsbKT;

namespace Osb.Shell.Userspace
{
    // Phase 1c/2 shell: kernel-owned session identity plus the slice-2 line
    // editor. Verifies the kernel ABI (>= 4), runs the login flow, then a
    // command loop with the `user@OSB cwd>` prompt. Commands: WHOAMI, LOGIN,
    // LOGOUT, SUDO <cmd>, ECHO <text>, HISTORY [N], EXIT, plus "(:N)" history
    // recall. Editing (arrows/Home/End/Delete/Backspace), Up/Down history and
    // TAB completion are handled by LineEditor/History/Completion; dispatch is
    // table-driven (Commands). Zero GC allocations (stack buffers only; osbkt
    // is called through its byte*/SpawnReq overloads).
    internal static unsafe class Program
    {
        private const int LineCap = 128;
        private const int NameCap = 32;
        private const int CwdCap = 256;

        private static void Main()
        {
            if (Syscall.GetABI() < Syscall.AbiVersion)
            {
                Syscall.PrintString("SHELL: kernel ABI too old (need >= ");
                PrintUShort(Syscall.AbiVersion);
                Syscall.PrintString("), halting.\r\n");
                return;
            }

            Commands.Init();
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
            var loginP = stackalloc char[8];
            var passP = stackalloc char[11];
            int lpLen = CopyLit(loginP, "login: ");      // 7
            int ppLen = CopyLit(passP, "Password: ");    // 10

            for (;;)
            {
                int nlen = LineEditor.Read(loginP, lpLen, name, NameCap, true, false);
                if (nlen <= 0) continue;   // aborted or empty: re-prompt
                Narrow(name, nbuf, nlen);  // char (UTF-16) -> byte string

                if (Syscall.Login(nbuf, nlen, null, 0) == 0) { Syscall.PutChar('\n'); return; }

                int plen = LineEditor.Read(passP, ppLen, pass, NameCap, false, false);
                Syscall.PutChar('\n');   // the extra newline the legacy flow prints
                if (plen < 0) continue;
                Narrow(pass, pbuf, plen);

                if (Syscall.Login(nbuf, nlen, pbuf, plen) == 0) { Syscall.PutChar('\n'); return; }
                Syscall.PrintString("Login incorrect\r\n");
            }
        }

        // Command loop. Each line is read with the full line editor (history +
        // completion), then "(:N)" recall is honoured, the line is recorded in
        // history, and the first matching command runs.
        private static void Session()
        {
            var line = stackalloc char[LineCap];
            var prompt = stackalloc char[CwdCap + NameCap + 8];
            var rc = stackalloc char[History.LineCap];   // recall target (once)
            for (;;)
            {
                int plen = BuildPrompt(prompt);
                int len = LineEditor.Read(prompt, plen, line, LineCap, true, true);
                if (len < 0) continue;   // Ctrl+C: re-prompt
                if (len == 0) continue;  // empty line: re-prompt (CRLF already echoed)

                if (History.LooksLikeRecall(line, len))
                {
                    int rlen;
                    if (History.ParseRecall(line, len, rc, out rlen))
                    {
                        Syscall.Print(rc, rlen);           // echo the line being re-run
                        Syscall.PutChar('\r'); Syscall.PutChar('\n');
                        History.Push(rc, rlen);            // record (consecutive dups skipped)
                        Dispatch(rc, rlen);
                    }
                    else
                    {
                        Syscall.PrintString("No such history entry\r\n");
                    }
                    continue;
                }

                History.Push(line, len);
                Dispatch(line, len);
            }
        }

        // First command that matches the line's first token. EXIT exits;
        // LOGIN/LOGOUT re-enter Login() in place; SUDO runs one command as root.
        private static void Dispatch(char* line, int len)
        {
            switch (Commands.Identify(line, len))
            {
                case Commands.EXIT:
                    Syscall.PrintString("bye\r\n");
                    Syscall.Exit(0);
                    return;
                case Commands.WHOAMI: DoWhoAmI(); return;
                case Commands.LOGIN: Login(); return;
                case Commands.LOGOUT: Syscall.Logout(); Login(); return;
                case Commands.SUDO: DoSudo(line, len); return;
                case Commands.HISTORY: DoHistory(line, len); return;
                case Commands.ECHO: DoEcho(line, len); return;
                default: return;   // NONE (known, unimplemented) or unknown: re-prompt
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

        // ECHO [text]: prints the text after "ECHO" (leading spaces skipped).
        private static void DoEcho(char* line, int len)
        {
            int i = 4;   // skip "ECHO"
            while (i < len && line[i] == ' ') i++;
            if (i < len) Syscall.Print(line + i, len - i);
            Syscall.PutChar('\r'); Syscall.PutChar('\n');
        }

        // HISTORY [N]: list all entries, or the last N. Format "%5d  <line>".
        private static void DoHistory(char* line, int len)
        {
            int count = -1;   // -1 = all
            int i = 7;        // skip "HISTORY"
            while (i < len && line[i] == ' ') i++;
            if (i < len)
            {
                count = 0;
                bool any = false;
                while (i < len && line[i] >= '0' && line[i] <= '9')
                {
                    count = count * 10 + (line[i] - '0'); i++; any = true;
                }
                if (!any) count = -1;
            }

            int total = History.Count;
            int start = 0;
            if (count >= 0 && count < total) start = total - count;

            var num = stackalloc char[8];
            var ebuf = stackalloc char[History.LineCap];
            for (int idx = start; idx < total; idx++)
            {
                int nl = Fmt.U32(num, (uint)History.NumberOf(idx));
                for (int k = nl; k < 5; k++) Syscall.PutChar(' ');
                Syscall.Print(num, nl);
                Syscall.PrintString("  ");
                int elen;
                History.CopyVisible(idx, ebuf, out elen);
                Syscall.Print(ebuf, elen);
                Syscall.PutChar('\r'); Syscall.PutChar('\n');
            }
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

        // "<user>@OSB <cwd>>" into dst; returns its length.
        private static int BuildPrompt(char* dst)
        {
            var ubuf = stackalloc byte[NameCap];
            int ulen = Syscall.WhoAmI(ubuf, NameCap);
            if (ulen < 0) ulen = 0;
            int n = 0;
            for (int k = 0; k < ulen; k++) dst[n++] = (char)ubuf[k];
            fixed (char* p = "@OSB ") for (int k = 0; k < 5; k++) dst[n++] = p[k];

            var cbuf = stackalloc byte[CwdCap];
            int clen = Syscall.GetCwd(cbuf, CwdCap);
            if (clen < 0) clen = 0;
            for (int k = 0; k < clen; k++) dst[n++] = (char)cbuf[k];
            dst[n++] = '>';
            return n;
        }

        // Copy a string literal into a stack buffer; returns the length.
        private static int CopyLit(char* dst, string s)
        {
            int n = s.Length;
            fixed (char* p = s)
            {
                for (int k = 0; k < n; k++) dst[k] = p[k];
            }
            return n;
        }

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