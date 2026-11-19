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
    internal static unsafe partial class Program
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
            Login(null, 0);
            Session();   // returns only on EXIT (which exits the process)
        }

        // Interactive login: "login:" and, for password-protected accounts,
        // "Password:" (no echo). The kernel decides whether a password is
        // needed: we try an empty password first; a -3 means either an unknown
        // user or a user that has a password, so we prompt and retry (the two
        // cases stay indistinguishable to the caller — no user enumeration).
        private static void DoLogout(char* line, int len)
        {
            int i = 6;   // skip "LOGOUT"
            while (i < len && line[i] == ' ') i++;
            if (i < len && line[i] == '/')
            {
                i++;
                if (i < len && line[i] == '?')
                {
                    Syscall.PrintString("LOGOUT [log out the current user]\r\n");
                    Syscall.PrintString("/? [show this help]\r\n");
                    return;
                }
            }
            Syscall.Logout();
            Login(line, len);
        }

        // Command loop. Each line is read with the full line editor (history +
        // completion), then "(:N)" recall is honoured, the line is recorded in
        // history, and the first matching command runs.
        private static void Dispatch(char* line, int len)
        {
            int id = Commands.Identify(line, len);
            switch (id)
            {
                case Commands.EXIT:
                    Syscall.PrintString("bye\r\n");
                    Syscall.Exit(0);
                    return;
                case Commands.WHOAMI: DoWhoAmI(line, len); return;
                case Commands.LOGIN: Login(line, len); return;
                case Commands.LOGOUT: DoLogout(line, len); return;
                case Commands.SUDO: DoSudo(line, len); return;
                case Commands.HISTORY: DoHistory(line, len); return;
                case Commands.ECHO: DoEcho(line, len); return;
                case Commands.SET: DoSet(line, len); return;
                case Commands.DIR: FileCommands.DoDir(line, len); return;
                case Commands.CD: FileCommands.DoCd(line, len); return;
                case Commands.PWD: FileCommands.DoPwd(line, len); return;
                case Commands.TYPE: FileCommands.DoType(line, len); return;
                case Commands.SIZE: FileCommands.DoSize(line, len); return;
                case Commands.MD: FileCommands.DoMd(line, len); return;
                case Commands.RD: FileCommands.DoRd(line, len); return;
                case Commands.DEL: FileCommands.DoDel(line, len); return;
                case Commands.REN: FileCommands.DoRen(line, len); return;
                case Commands.COPY: FileCommands.DoCopy(line, len); return;
                case Commands.LN: FileCommands.DoLn(line, len); return;
                case Commands.TRASH: FileCommands.DoTrash(line, len); return;
                case Commands.VER: DoVer(line, len); return;
                case Commands.HELP: DoHelp(line, len); return;
                case Commands.CLS: DoCls(line, len); return;
                case Commands.MEM: DoMem(line, len); return;
                case Commands.REBOOT: DoReboot(line, len); return;
                case Commands.TIME: DoTime(line, len); return;
                case Commands.DATE: DoDate(line, len); return;
                case Commands.TICKS: DoTicks(line, len); return;
                case Commands.SLEEP: DoSleep(line, len); return;
                case Commands.USER: DoUser(line, len); return;
                case Commands.CHOWN: DoChown(line, len); return;
                case Commands.CHMOD: DoChmod(line, len); return;
                case Commands.TASK: DoTask(line, len); return;
                case Commands.KILL: DoKill(line, len); return;
                case Commands.WAIT: DoWait(line, len); return;
                case Commands.TAB: DoTab(line, len); return;
                case Commands.SPAWN: DoSpawn(line, len); return;
                default:
                    // Unknown first token: an external program (C3). A known
                    // but not-yet-implemented command (NONE) just re-prompts.
                    if (id < 0) External.Run(line, len);
                    return;
            }
        }

        private static void DoWhoAmI(char* line, int len)
        {
            int i = 6;   // skip "WHOAMI"
            while (i < len && line[i] == ' ') i++;
            if (i < len && line[i] == '/')
            {
                i++;
                if (i < len && line[i] == '?')
                {
                    Syscall.PrintString("WHOAMI [show current user name]\r\n");
                    Syscall.PrintString("/? [show this help]\r\n");
                    return;
                }
            }
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
            if (i < len && line[i] == '/')
            {
                i++;
                if (i < len && line[i] == '?')
                {
                    Syscall.PrintString("ECHO [text] [print text]\r\n");
                    Syscall.PrintString("/? [show this help]\r\n");
                    return;
                }
            }
            if (i < len) Syscall.Print(line + i, len - i);
            Syscall.PutChar('\r'); Syscall.PutChar('\n');
        }

        // HISTORY [/N <n>]: list the last N entries (default 100). Format "%5d  <line>".
        // A bare number (`HISTORY 2`) is treated as the count, per MIGRATION §1.3.
        private static void DoHistory(char* line, int len)
        {
            int i = 7;        // skip "HISTORY"
            while (i < len && line[i] == ' ') i++;
            if (i < len && line[i] == '/')
            {
                i++;
                if (i < len && line[i] == '?')
                {
                    Syscall.PrintString("HISTORY [/N <n>] [list command history]\r\n");
                    Syscall.PrintString("  default: last 100 entries\r\n");
                    Syscall.PrintString("/N <n> [show last n entries]\r\n");
                    Syscall.PrintString("/? [show this help]\r\n");
                    return;
                }
            }
            int count = 100;
            bool anyCount = false;
            if (i < len && (line[i] == 'N' || line[i] == 'n'))
            {
                i++;
                while (i < len && line[i] == ' ') i++;
                anyCount = true;
            }
            else if (i < len && line[i] >= '0' && line[i] <= '9')
            {
                anyCount = true;
            }
            while (i < len && line[i] >= '0' && line[i] <= '9')
            {
                count = count * 10 + (line[i] - '0'); i++; anyCount = true;
            }
            if (!anyCount || count <= 0) count = 100;
            int total = History.Count;
            int start = count < total ? total - count : 0;
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
            if (i < len && line[i] == '/')
            {
                i++;
                if (i < len && line[i] == '?')
                {
                    Syscall.PrintString("SUDO <command> [run command as root]\r\n");
                    Syscall.PrintString("/? [show this help]\r\n");
                    return;
                }
            }
            if (i >= len) { Syscall.PrintString("Usage: SUDO <command>\r\n"); return; }

            // Password prompt unless already root.
            var who = stackalloc byte[NameCap];
            int wn = Syscall.WhoAmI(who, NameCap);
            bool isRoot = wn == 4 && who[0] == (byte)'r' && who[1] == (byte)'o'
                       && who[2] == (byte)'o' && who[3] == (byte)'t';
            if (!isRoot)
            {
                var passP = stackalloc char[11];
                int ppLen = CopyLit(passP, "Password: ");   // 10
                var pass = stackalloc char[NameCap];
                int plen = LineEditor.Read(passP, ppLen, pass, NameCap, false, false);
                Syscall.PutChar('\n');
                if (plen < 0) { Syscall.PrintString("SUDO: aborted\r\n"); return; }
                var pbuf = stackalloc byte[NameCap];
                Narrow(pass, pbuf, plen);
                // verify password by re-logging in as the current user
                var nbuf = stackalloc byte[NameCap];
                Narrow((char*)who, nbuf, wn);
                if (Syscall.Login(nbuf, wn, pbuf, plen) != 0)
                {
                    Syscall.PrintString("SUDO: password incorrect\r\n");
                    return;
                }
            }

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
            return SpawnCmdHandles(cmd, clen, -1, -1);
        }

        // Spawn with custom stdin/stdout handles (for pipes).
        private static int SpawnCmdHandles(char* cmd, int clen, int stdinHandle, int stdoutHandle)
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
                StdinHandle = stdinHandle,
                StdoutHandle = stdoutHandle
            };
            return Syscall.SpawnPath(&req);
        }

        // SET [NAME[=VALUE]]: no args lists all, NAME lists matching entries,
        // NAME=VALUE defines/redefines. Over 63h/64h (ABI v5).
        private static void DoSet(char* line, int len)
        {
            int i = 3;   // skip "SET"
            while (i < len && line[i] == ' ') i++;
            if (i < len && line[i] == '/')
            {
                i++;
                if (i < len && line[i] == '?')
                {
                    Syscall.PrintString("SET [NAME[=VALUE]] [get/set environment]\r\n");
                    Syscall.PrintString("/? [show this help]\r\n");
                    return;
                }
            }
            if (i >= len) { ListEnv(null, 0); return; }
            int eq = i;
            while (eq < len && line[eq] != '=') eq++;
            if (eq >= len) { ListEnv(line + i, len - i); return; }

            int nl = eq - i;
            int vs = eq + 1;
            int vl = len - vs;
            var nb = stackalloc byte[64];
            var vb = stackalloc byte[256];
            for (int k = 0; k < nl && k < 63; k++) nb[k] = (byte)line[i + k];
            for (int k = 0; k < vl && k < 255; k++) vb[k] = (byte)line[vs + k];
            if (nl <= 0 || nl > 63 || vl > 255 || !Syscall.SetEnv(nb, nl, vb, vl))
                Syscall.PrintString("Cannot set: read-only or out of memory.\r\n");
        }

        // SET listing: 63h with nameLen 0 returns "NAME=VALUE\n" lines; print
        // the ones whose name starts with `prefix` (case-insensitive), or all.
        private static void ListEnv(char* prefix, int plen)
        {
            var buf = stackalloc byte[1024];
            int n = Syscall.GetEnv(null, 0, buf, 1024);
            bool any = false;
            int i = 0;
            while (i < n)
            {
                int st = i;
                while (i < n && buf[i] != (byte)'\n') i++;
                int lineLen = i - st;
                if (i < n) i++;
                if (plen > 0)
                {
                    bool match = lineLen >= plen;
                    for (int k = 0; match && k < plen; k++)
                        if (Commands.Upper((char)buf[st + k]) != Commands.Upper(prefix[k])) match = false;
                    if (!match) continue;
                }
                Syscall.Write(buf + st, lineLen);
                Syscall.PutChar('\r');
                Syscall.PutChar('\n');
                any = true;
            }
            if (!any && plen > 0) Syscall.PrintString("Environment variable not defined.\r\n");
        }

        // "<user>@OSB <cwd>>" into dst; returns its length. $PROMPT driven
        // (Slice 3): the default is "$USER@OSB $P$G".
        private static int BuildPrompt(char* dst)
        {
            return Env.RenderPrompt(dst, Env.PromptCap);
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
