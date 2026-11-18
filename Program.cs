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
            Login(null, 0);
            Session();   // returns only on EXIT (which exits the process)
        }

        // Interactive login: "login:" and, for password-protected accounts,
        // "Password:" (no echo). The kernel decides whether a password is
        // needed: we try an empty password first; a -3 means either an unknown
        // user or a user that has a password, so we prompt and retry (the two
        // cases stay indistinguishable to the caller — no user enumeration).
        private static void Login(char* line, int len)
        {
            int i = 5;   // skip "LOGIN"
            while (i < len && line[i] == ' ') i++;
            if (i < len && line[i] == '/')
            {
                i++;
                if (i < len && line[i] == '?')
                {
                    Syscall.PrintString("LOGIN [log in as a user]\r\n");
                    Syscall.PrintString("/? [show this help]\r\n");
                    return;
                }
            }
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
                // Expand $VAR references ($USER, $HOME, ...) before dispatch.
                // Undefined names round-trip, so SET PROMPT=$P$G keeps its tokens.
                var expanded = stackalloc char[Env.ExpandCap];
                int elen = Env.Expand(line, len, expanded, Env.ExpandCap);
                if (elen > 0)
                {
                    // Pipe: "left | right" — run left with stdout → temp file,
                    // then right with stdin ← temp file (external programs only).
                    int pipePos = -1;
                    for (int k = 0; k < elen; k++)
                        if (expanded[k] == '|') { pipePos = k; break; }
                    if (pipePos >= 0) RunPipe(expanded, elen, pipePos);
                    else Dispatch(expanded, elen);
                }
            }
        }

        // Run "left | right" using a temp file for data transfer.
        // Built-in commands are supported on both sides:
        //   left built-in: stdout redirected to temp file via SetStdout
        //   right built-in: runs normally (stdin not connected)
        private static void RunPipe(char* line, int len, int pipePos)
        {
            int ls = 0;
            while (ls < pipePos && line[ls] == ' ') ls++;
            int le = pipePos;
            while (le > ls && line[le - 1] == ' ') le--;
            int ll = le - ls;

            int rs = pipePos + 1;
            while (rs < len && line[rs] == ' ') rs++;
            int re = len;
            while (re > rs && line[re - 1] == ' ') re--;
            int rl = re - rs;

            if (ll <= 0 || rl <= 0) { Syscall.PrintString("Bad command or file name.\r\n"); return; }

            // temp file path: A:\OSB\TEMP\PIPE.TMP
            var tmpPath = stackalloc byte[64];
            int tmplen = 0;
            tmpPath[tmplen++] = (byte)'A'; tmpPath[tmplen++] = (byte)':';
            tmpPath[tmplen++] = (byte)'\\'; tmpPath[tmplen++] = (byte)'O';
            tmpPath[tmplen++] = (byte)'S'; tmpPath[tmplen++] = (byte)'B';
            tmpPath[tmplen++] = (byte)'\\'; tmpPath[tmplen++] = (byte)'T';
            tmpPath[tmplen++] = (byte)'E'; tmpPath[tmplen++] = (byte)'M';
            tmpPath[tmplen++] = (byte)'P'; tmpPath[tmplen++] = (byte)'\\';
            tmpPath[tmplen++] = (byte)'P'; tmpPath[tmplen++] = (byte)'I';
            tmpPath[tmplen++] = (byte)'P'; tmpPath[tmplen++] = (byte)'E';
            tmpPath[tmplen++] = (byte)'.'; tmpPath[tmplen++] = (byte)'T';
            tmpPath[tmplen++] = (byte)'M'; tmpPath[tmplen++] = (byte)'P';

            int h1 = Syscall.CreateFile(tmpPath, tmplen);
            if (h1 < 0) { Syscall.PrintString("Pipe: cannot create temp file\r\n"); return; }

            // Left side: built-in (redirect stdout to temp file) or external (spawn with stdout handle)
            int leftId = Commands.Identify(line + ls, ll);
            if (leftId >= 0 && leftId != Commands.NONE)
            {
                Syscall.SetStdout(h1);
                Dispatch(line + ls, ll);
                Syscall.SetStdout(-1);
            }
            else if (leftId < 0)
            {
                int pid1 = SpawnCmdHandles(line + ls, ll, -1, h1);
                if (pid1 < 0)
                {
                    Syscall.Close(h1);
                    Syscall.PrintString("Pipe: cannot run left command\r\n");
                    return;
                }
                Syscall.Wait(pid1);
            }
            // NONE: known but not implemented — skip

            Syscall.Close(h1);

            int h2 = Syscall.Open(tmpPath, tmplen);
            if (h2 < 0)
            {
                Syscall.PrintString("Pipe: cannot read temp file\r\n");
                return;
            }

            // Right side: built-in (runs normally) or external (spawn with stdin handle)
            int rightId = Commands.Identify(line + rs, rl);
            if (rightId >= 0 && rightId != Commands.NONE)
            {
                Dispatch(line + rs, rl);
            }
            else if (rightId < 0)
            {
                int pid2 = SpawnCmdHandles(line + rs, rl, h2, -1);
                if (pid2 < 0)
                {
                    Syscall.Close(h2);
                    Syscall.PrintString("Pipe: cannot run right command\r\n");
                    return;
                }
                Syscall.Wait(pid2);
            }

            Syscall.Close(h2);
            // Kernel delete syscall expects "DEL <path>" format.
            var delBuf = stackalloc byte[64];
            int dn = 0;
            delBuf[dn++] = (byte)'D'; delBuf[dn++] = (byte)'E'; delBuf[dn++] = (byte)'L'; delBuf[dn++] = (byte)' ';
            for (int k = 0; k < tmplen && dn < 63; k++) delBuf[dn++] = tmpPath[k];
            Syscall.Delete(delBuf, dn);
        }

        // First command that matches the line's first token. EXIT exits;
        // LOGIN/LOGOUT re-enter Login() in place; SUDO runs one command as root.
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
                if (i < len && line[i] == 'N' || line[i] == 'n')
                {
                    i++;
                    while (i < len && line[i] == ' ') i++;
                    int count = 0;
                    bool any = false;
                    while (i < len && line[i] >= '0' && line[i] <= '9')
                    {
                        count = count * 10 + (line[i] - '0'); i++; any = true;
                    }
                    if (!any || count <= 0) count = 100;
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
                    return;
                }
            }
            // default: last 100 entries
            int count0 = 100;
            int total0 = History.Count;
            int start0 = count0 < total0 ? total0 - count0 : 0;
            var num0 = stackalloc char[8];
            var ebuf0 = stackalloc char[History.LineCap];
            for (int idx = start0; idx < total0; idx++)
            {
                int nl = Fmt.U32(num0, (uint)History.NumberOf(idx));
                for (int k = nl; k < 5; k++) Syscall.PutChar(' ');
                Syscall.Print(num0, nl);
                Syscall.PrintString("  ");
                int elen;
                History.CopyVisible(idx, ebuf0, out elen);
                Syscall.Print(ebuf0, elen);
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

        // ---- slice 9: system & time commands ----

        private static void DoVer(char* line, int len)
        {
            int i = 0;
            while (i < len && line[i] == ' ') i++;
            int ts = i;
            while (i < len && line[i] != ' ') i++;

            // Parse flags: concatenated (/A/S/O) or separate (/A /S /O)
            bool showKernel = false;
            bool showShell = false;
            bool showAbout = false;
            bool showHelp = false;
            bool anyFlag = false;

            while (i < len)
            {
                while (i < len && line[i] == ' ') i++;
                if (i >= len) break;
                int fs = i;
                while (i < len && line[i] != ' ') i++;
                int fk = fs;
                while (fk < i)
                {
                    if (line[fk] == '/')
                    {
                        fk++;
                        if (fk < i)
                        {
                            anyFlag = true;
                            char f = line[fk];
                            if (f >= 'a' && f <= 'z') f -= (char)32;
                            switch (f)
                            {
                                case 'O': showKernel = true; break;
                                case 'S': showShell = true; break;
                                case 'A': showAbout = true; break;
                                case '?': showHelp = true; break;
                            }
                            fk++;
                        }
                    }
                    else
                    {
                        fk++;
                    }
                }
            }

            if (showHelp)
            {
                Syscall.PrintString("VER [show OSB version information]\r\n");
                Syscall.PrintString("/O [show kernel version]\r\n");
                Syscall.PrintString("/S [show shell version]\r\n");
                Syscall.PrintString("/A [show about information]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }

            if (!anyFlag) showKernel = true; // default: kernel version

            if (showKernel)
                Syscall.PrintString("OSB (dotnet) version 0.1 - bootable kernel\r\n");
            if (showShell)
                Syscall.PrintString("OSB shell version 0.1\r\n");
            if (showAbout)
                Syscall.PrintString("OSB: Operating System Basic, 2000-2026\r\n");
        }

        // HELP [<cmd>]: per-command page, or the overview.
        private static void DoHelp(char* line, int len)
        {
            int i = 4;   // skip "HELP"
            while (i < len && line[i] == ' ') i++;
            if (i >= len)
            {
                Syscall.PrintString("OSB shell commands:\r\n");
                Syscall.PrintString("  VER HELP CLS CLEAR MEM REBOOT\r\n");
                Syscall.PrintString("  TIME DATE TICKS SLEEP ECHO SET DIR CD PWD TYPE SIZE MD RD\r\n");
                Syscall.PrintString("  DEL REN MOVE COPY CP SIZE LN\r\n");
                Syscall.PrintString("  TRASH GREP PING PCI NET IFCONFIG USER LOGIN LOGOUT WHOAMI\r\n");
                Syscall.PrintString("  CHOWN CHMOD SUDO HISTORY TASK PS KILL WAIT TAB SPAWN PIPE\r\n");
                Syscall.PrintString("  EXIT LOGOUT\r\n");
                return;
            }
            // find the command name token
            int s = i, e = i;
            while (e < len && line[e] != ' ') e++;
            int tl = e - s;

            // Match command name (case-insensitive)
            bool match(int slen, string lit)
            {
                if (tl != slen) return false;
                fixed (char* p = lit)
                {
                    for (int k = 0; k < tl; k++)
                    {
                        char c = line[s + k];
                        if (c >= 'a' && c <= 'z') c -= (char)32;
                        if (c != p[k]) return false;
                    }
                }
                return true;
            }

            if (match(3, "VER"))
            {
                Syscall.PrintString("VER [show OSB version information]\r\n");
                Syscall.PrintString("/O [show kernel version]\r\n");
                Syscall.PrintString("/S [show shell version]\r\n");
                Syscall.PrintString("/A [show about information]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(3, "CLS"))
            {
                Syscall.PrintString("CLS [clear the screen]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(3, "MEM"))
            {
                Syscall.PrintString("MEM [show memory information]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(4, "TIME"))
            {
                Syscall.PrintString("TIME [show uptime]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(4, "DATE"))
            {
                Syscall.PrintString("DATE [show current date and time]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(5, "TICKS"))
            {
                Syscall.PrintString("TICKS [show tick count]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(5, "SLEEP"))
            {
                Syscall.PrintString("SLEEP <seconds> [sleep for N seconds]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(4, "ECHO"))
            {
                Syscall.PrintString("ECHO [text] [print text]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(3, "SET"))
            {
                Syscall.PrintString("SET [NAME[=VALUE]] [get/set environment]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(3, "DIR"))
            {
                Syscall.PrintString("DIR [path] [/W] [list directory contents]\r\n");
                Syscall.PrintString("  long: type perms owner size name\r\n");
                Syscall.PrintString("/W [wide format]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(2, "CD"))
            {
                Syscall.PrintString("CD [path] [change directory]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(3, "PWD"))
            {
                Syscall.PrintString("PWD [print working directory]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(4, "TYPE"))
            {
                Syscall.PrintString("TYPE <file> [display file contents]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(4, "SIZE"))
            {
                Syscall.PrintString("SIZE <file> [show file size]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(2, "MD"))
            {
                Syscall.PrintString("MD <dir> [create directory]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(2, "RD"))
            {
                Syscall.PrintString("RD <dir> [remove directory]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(3, "DEL"))
            {
                Syscall.PrintString("DEL <file> [delete file]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(3, "REN"))
            {
                Syscall.PrintString("REN old new [rename file]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(4, "COPY"))
            {
                Syscall.PrintString("COPY src dst [copy file]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(2, "LN"))
            {
                Syscall.PrintString("LN [/S] <link> <target> [create link]\r\n");
                Syscall.PrintString("/S [create symbolic link]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(5, "TRASH"))
            {
                Syscall.PrintString("TRASH [/F pattern] [/U name] [manage trash]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(6, "REBOOT"))
            {
                Syscall.PrintString("REBOOT [reboot the system]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(4, "USER"))
            {
                Syscall.PrintString("USER [/ADD name [pass] | /CHANGE name [pass] | /DEL name [/HOME] | /SUDO name | /UNSUDO name]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(5, "CHOWN"))
            {
                Syscall.PrintString("CHOWN <user> <path> [change file owner, root only]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(5, "CHMOD"))
            {
                Syscall.PrintString("CHMOD <octal> <path> [change file mode, owner or root]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(4, "TASK"))
            {
                Syscall.PrintString("TASK [&] [list tasks or run in background]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(4, "KILL"))
            {
                Syscall.PrintString("KILL <pid> [terminate a process]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(4, "WAIT"))
            {
                Syscall.PrintString("WAIT <pid> [wait for a process]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(3, "TAB"))
            {
                Syscall.PrintString("TAB <pid> [set foreground process]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(5, "SPAWN"))
            {
                Syscall.PrintString("SPAWN <program> [&] [run an external program]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(6, "WHOAMI"))
            {
                Syscall.PrintString("WHOAMI [show current user name]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(5, "LOGIN"))
            {
                Syscall.PrintString("LOGIN [log in as a user]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(6, "LOGOUT"))
            {
                Syscall.PrintString("LOGOUT [log out the current user]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(4, "SUDO"))
            {
                Syscall.PrintString("SUDO <command> [run command as root]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(7, "HISTORY"))
            {
                Syscall.PrintString("HISTORY [/N N] [list command history]\r\n");
                Syscall.PrintString("  default: last 100 entries\r\n");
                Syscall.PrintString("/N <n> [show last n entries]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(4, "HELP"))
            {
                Syscall.PrintString("HELP [<cmd>] [show command help]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(4, "EXIT"))
            {
                Syscall.PrintString("EXIT [exit the shell]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(4, "PIPE"))
            {
                Syscall.PrintString("PIPE: left | right  [run left, pipe stdout to right stdin]\r\n");
                Syscall.PrintString("  both built-in and external programs are supported\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(5, "CLEAR"))
            {
                Syscall.PrintString("CLEAR [clear the screen]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(4, "MOVE"))
            {
                Syscall.PrintString("MOVE old new [rename file]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(2, "CP"))
            {
                Syscall.PrintString("CP src dst [copy file]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(2, "PS"))
            {
                Syscall.PrintString("PS [list tasks]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(3, "NET"))
            {
                Syscall.PrintString("NET [show network status]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(8, "IFCONFIG"))
            {
                Syscall.PrintString("IFCONFIG [show network interfaces]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(4, "GREP"))
            {
                Syscall.PrintString("GREP <pattern> <file> [search file for pattern]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(4, "PING"))
            {
                Syscall.PrintString("PING <host> [send ICMP echo]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (match(3, "PCI"))
            {
                Syscall.PrintString("PCI [list PCI devices]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            // Simple: print the same page as '/?' for known commands (minimal).
            Syscall.PrintString("No help for ");
            Syscall.Print(line + s, tl);
            Syscall.PrintString("\r\n");
        }

        private static void DoCls(char* line, int len)
        {
            int i = 4;   // skip "CLS"
            while (i < len && line[i] == ' ') i++;
            if (i < len && line[i] == '/')
            {
                i++;
                if (i < len)
                {
                    char f = line[i];
                    if (f >= 'a' && f <= 'z') f -= (char)32;
                    if (f == '?')
                    {
                        Syscall.PrintString("CLS [clear the screen]\r\n");
                        Syscall.PrintString("/? [show this help]\r\n");
                        return;
                    }
                }
            }
            Syscall.Clear();
        }

        private static void DoMem(char* line, int len)
        {
            int i = 3;   // skip "MEM"
            while (i < len && line[i] == ' ') i++;
            if (i < len && line[i] == '/')
            {
                i++;
                if (i < len && line[i] == '?')
                {
                    Syscall.PrintString("MEM [show memory information]\r\n");
                    Syscall.PrintString("/? [show this help]\r\n");
                    return;
                }
            }
            var buf = stackalloc ulong[3];
            Syscall.MemInfo(out buf[0], out buf[1], out buf[2]);
            Syscall.PrintString("Physical frames: ");
            PrintULong(buf[0] / 4);   // rough: KB/4 -> frames used; print total
            Syscall.PrintString(" used / ");
            PrintULong(buf[2] / 4);
            Syscall.PrintString(" total (");
            PrintULong(buf[0] / 1024);
            Syscall.PrintString(" MiB usable)\r\n");
        }

        private static void DoReboot(char* line, int len)
        {
            int i = 6;   // skip "REBOOT"
            while (i < len && line[i] == ' ') i++;
            if (i < len && line[i] == '/')
            {
                i++;
                if (i < len && line[i] == '?')
                {
                    Syscall.PrintString("REBOOT [reboot the system]\r\n");
                    Syscall.PrintString("/? [show this help]\r\n");
                    return;
                }
            }
            Syscall.PrintString("Rebooting ...\r\n");
        }

        private static void DoTime(char* line, int len)
        {
            int i = 4;   // skip "TIME"
            while (i < len && line[i] == ' ') i++;
            if (i < len && line[i] == '/')
            {
                i++;
                if (i < len && line[i] == '?')
                {
                    Syscall.PrintString("TIME [show uptime]\r\n");
                    Syscall.PrintString("/? [show this help]\r\n");
                    return;
                }
            }
            Syscall.PrintString("Uptime: ");
            PrintULong(Syscall.Ticks() / 100);
            Syscall.PrintString(" s (");
            PrintULong(Syscall.Ticks());
            Syscall.PrintString(" ticks)\r\n");
        }

        private static void DoDate(char* line, int len)
        {
            int i = 4;   // skip "DATE"
            while (i < len && line[i] == ' ') i++;
            if (i < len && line[i] == '/')
            {
                i++;
                if (i < len && line[i] == '?')
                {
                    Syscall.PrintString("DATE [show current date and time]\r\n");
                    Syscall.PrintString("/? [show this help]\r\n");
                    return;
                }
            }
            Syscall.DateTime(out int y, out int mo, out int d, out int h, out int mi, out int s);
            PrintTwo(mo); Syscall.PutChar('/'); PrintTwo(d); Syscall.PrintString("/20");
            PrintTwo(y); Syscall.PutChar(' ');
            PrintTwo(h); Syscall.PutChar(':'); PrintTwo(mi); Syscall.PutChar(':'); PrintTwo(s);
            Syscall.PutChar('\r'); Syscall.PutChar('\n');
        }

        private static void DoTicks(char* line, int len)
        {
            int i = 5;   // skip "TICKS"
            while (i < len && line[i] == ' ') i++;
            if (i < len && line[i] == '/')
            {
                i++;
                if (i < len && line[i] == '?')
                {
                    Syscall.PrintString("TICKS [show tick count]\r\n");
                    Syscall.PrintString("/? [show this help]\r\n");
                    return;
                }
            }
            Syscall.PrintString("Ticks: ");
            PrintULong(Syscall.Ticks());
            Syscall.PutChar('\r'); Syscall.PutChar('\n');
        }

        private static void DoSleep(char* line, int len)
        {
            int i = 5;   // skip "SLEEP"
            while (i < len && line[i] == ' ') i++;
            if (i < len && line[i] == '/')
            {
                i++;
                if (i < len && line[i] == '?')
                {
                    Syscall.PrintString("SLEEP <seconds> [sleep for N seconds]\r\n");
                    Syscall.PrintString("/? [show this help]\r\n");
                    return;
                }
            }
            ulong ticks = 0;
            bool any = false;
            while (i < len && line[i] >= '0' && line[i] <= '9')
            {
                ticks = ticks * 10 + (ulong)(line[i] - '0'); i++; any = true;
            }
            if (!any) { Syscall.PrintString("Usage: SLEEP <seconds>\r\n"); return; }
            Syscall.PrintString("Sleeping ...\r\n");
            Syscall.Sleep(ticks * 100);   // ticks ~ 10ms each; seconds * 100
            Syscall.PrintString("Done.\r\n");
        }

        // ---- slice 11: COLOR ----

        // ---- slice 7: USER / CHOWN / CHMOD ----

        private static bool FlagEq(char* flag, int len, string lit)
        {
            if (len != lit.Length) return false;
            fixed (char* p = lit)
            {
                for (int i = 0; i < len; i++)
                    if (flag[i] != p[i]) return false;
            }
            return true;
        }

        private static void DoUserList()
        {
            var buf = stackalloc byte[512];
            int n = Syscall.UserOpList(buf, 512);
            if (n < 0) { Syscall.PrintString("USER: list failed\r\n"); return; }
            Syscall.PrintString("  NAME          UID   HOME                              FLAGS\r\n");
            int pos = 0;
            while (pos < n)
            {
                int ns = pos;
                while (pos < n && buf[pos] != ' ') pos++;
                int nl = pos - ns;
                if (pos < n) pos++;
                int us = pos;
                while (pos < n && buf[pos] != ' ') pos++;
                int ul = pos - us;
                if (pos < n) pos++;
                int hs = pos;
                while (pos < n && buf[pos] != ' ') pos++;
                int hl = pos - hs;
                if (pos < n) pos++;
                char sf = (pos < n) ? (char)buf[pos++] : (char)'-';
                if (pos < n) pos++;
                bool starred = (pos < n && buf[pos] == '*');
                if (starred) { pos++; if (pos < n && buf[pos] == ' ') pos++; }
                while (pos < n && buf[pos] != '\n') pos++;
                if (pos < n) pos++;

                Syscall.PrintString("  ");
                Syscall.Write(buf + ns, nl);
                for (int k = nl; k < 14; k++) Syscall.PutChar(' ');
                Syscall.Write(buf + us, ul);
                for (int k = ul; k < 5; k++) Syscall.PutChar(' ');
                Syscall.PrintString("  ");
                Syscall.Write(buf + hs, hl);
                for (int k = hl; k < 34; k++) Syscall.PutChar(' ');
                Syscall.PrintString(sf == '1' ? "sudo" : "-");
                if (starred) Syscall.PrintString(" *");
                Syscall.PutChar('\r'); Syscall.PutChar('\n');
            }
        }

        private static void DoUser(char* line, int len)
        {
            int i = 4;   // skip "USER"
            while (i < len && line[i] == ' ') i++;
            int fs = i;
            while (i < len && line[i] != ' ') i++;
            int fl = i - fs;

            if (fl == 0 || line[fs] != '/')
            {
                DoUserList();
                return;
            }

            char* flag = stackalloc char[16];
            int fn = fl < 15 ? fl : 15;
            for (int k = 0; k < fn; k++)
            {
                char c = line[fs + k];
                if (c >= 'a' && c <= 'z') c -= (char)32;
                flag[k] = c;
            }

            while (i < len && line[i] == ' ') i++;
            int argS = i;
            while (i < len && line[i] != ' ') i++;
            int argL = i - argS;

            if (FlagEq(flag, fn, "/ADD"))
            {
                if (argL <= 0) { Syscall.PrintString("Usage: USER /ADD <name> [password]\r\n"); return; }
                while (i < len && line[i] == ' ') i++;
                int ps = i;
                while (i < len && line[i] != ' ') i++;
                int plen = i - ps;
                var nbuf = stackalloc byte[260];
                var pbuf = stackalloc byte[260];
                Narrow(line + argS, nbuf, argL);
                Narrow(line + ps, pbuf, plen);
                int rc = Syscall.UserOpAdd(nbuf, argL, pbuf, plen);
                if (rc == 0) Syscall.PrintString("OK\r\n");
                else if (rc == -3) Syscall.PrintString("Permission denied\r\n");
                else Syscall.PrintString("Cannot add user\r\n");
                return;
            }
            if (FlagEq(flag, fn, "/CHANGE"))
            {
                if (argL <= 0) { Syscall.PrintString("Usage: USER /CHANGE <name> [password]\r\n"); return; }
                while (i < len && line[i] == ' ') i++;
                int ps = i;
                while (i < len && line[i] != ' ') i++;
                int plen = i - ps;
                var nbuf = stackalloc byte[260];
                var pbuf = stackalloc byte[260];
                Narrow(line + argS, nbuf, argL);
                Narrow(line + ps, pbuf, plen);
                int rc = Syscall.UserOpChangePass(nbuf, argL, pbuf, plen);
                if (rc == 0) Syscall.PrintString("Password changed\r\n");
                else if (rc == -3) Syscall.PrintString("Permission denied\r\n");
                else if (rc == -4) Syscall.PrintString("No such user\r\n");
                else Syscall.PrintString("Cannot change password\r\n");
                return;
            }
            if (FlagEq(flag, fn, "/DEL"))
            {
                if (argL <= 0) { Syscall.PrintString("Usage: USER /DEL <name> [/HOME]\r\n"); return; }
                var nbuf = stackalloc byte[260];
                Narrow(line + argS, nbuf, argL);
                // check for /HOME flag after the name (RDX bit 0)
                uint flags = 0;
                while (i < len && line[i] == ' ') i++;
                if (i < len && line[i] == '/' && i + 5 <= len)
                {
                    bool isHome = true;
                    char h0 = 'H', h1 = 'O', h2 = 'M', h3 = 'E';
                    for (int k = 0; k < 4; k++)
                    {
                        char c = line[i + 1 + k];
                        if (c >= 'a' && c <= 'z') c -= (char)32;
                        char hk = k == 0 ? h0 : k == 1 ? h1 : k == 2 ? h2 : h3;
                        if (c != hk) { isHome = false; break; }
                    }
                    if (isHome && (i + 5 == len || line[i + 5] == ' '))
                        flags = 1;
                }
                int rc = Syscall.UserOpDel(nbuf, argL, flags);
                if (rc == 0) Syscall.PrintString("OK\r\n");
                else if (rc == -3) Syscall.PrintString("Permission denied\r\n");
                else if (rc == -4) Syscall.PrintString("No such user\r\n");
                else if (rc == -5) Syscall.PrintString("Cannot delete root\r\n");
                else Syscall.PrintString("Error\r\n");
                return;
            }
            if (FlagEq(flag, fn, "/SUDO"))
            {
                if (argL <= 0) { Syscall.PrintString("Usage: USER /SUDO <name>\r\n"); return; }
                var nbuf = stackalloc byte[260];
                Narrow(line + argS, nbuf, argL);
                int rc = Syscall.UserOpSudo(nbuf, argL);
                if (rc == 0) Syscall.PrintString("OK\r\n");
                else if (rc == -3) Syscall.PrintString("Permission denied\r\n");
                else if (rc == -4) Syscall.PrintString("No such user\r\n");
                else Syscall.PrintString("Error\r\n");
                return;
            }
            if (FlagEq(flag, fn, "/UNSUDO"))
            {
                if (argL <= 0) { Syscall.PrintString("Usage: USER /UNSUDO <name>\r\n"); return; }
                var nbuf = stackalloc byte[260];
                Narrow(line + argS, nbuf, argL);
                int rc = Syscall.UserOpUnsudo(nbuf, argL);
                if (rc == 0) Syscall.PrintString("OK\r\n");
                else if (rc == -3) Syscall.PrintString("Permission denied\r\n");
                else if (rc == -4) Syscall.PrintString("No such user\r\n");
                else Syscall.PrintString("Error\r\n");
                return;
            }
            if (FlagEq(flag, fn, "/?"))
            {
                Syscall.PrintString("USER [/ADD name [pass] | /CHANGE name [pass] | /DEL name | /SUDO name | /UNSUDO name]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }

            Syscall.PrintString("Unknown USER flag. Use /ADD /CHANGE /DEL /SUDO /UNSUDO.\r\n");
        }

        private static void DoChown(char* line, int len)
        {
            int i = 5;   // skip "CHOWN"
            while (i < len && line[i] == ' ') i++;
            if (i < len && line[i] == '/')
            {
                i++;
                if (i < len && line[i] == '?')
                {
                    Syscall.PrintString("CHOWN <user> <path> [change file owner, root only]\r\n");
                    Syscall.PrintString("/? [show this help]\r\n");
                    return;
                }
            }
            int us = i;
            while (i < len && line[i] != ' ') i++;
            int ul = i - us;
            if (ul <= 0) { Syscall.PrintString("Usage: CHOWN <user> <path>\r\n"); return; }

            // resolve username -> uid via USEROP LIST
            var buf = stackalloc byte[512];
            int n = Syscall.UserOpList(buf, 512);
            if (n < 0) { Syscall.PrintString("CHOWN: list failed\r\n"); return; }
            int uid = -1;
            int pos = 0;
            while (pos < n)
            {
                int ns = pos;
                while (pos < n && buf[pos] != ' ') pos++;
                int nl = pos - ns;
                if (pos < n) pos++;
                if (nl == ul)
                {
                    bool match = true;
                    for (int k = 0; k < nl; k++)
                    {
                        char a = line[us + k];
                        if (a >= 'a' && a <= 'z') a -= (char)32;
                        char b = (char)buf[ns + k];
                        if (b >= 'a' && b <= 'z') b -= (char)32;
                        if (a != b) { match = false; break; }
                    }
                    if (match)
                    {
                        if (pos < n) pos++;
                        int us2 = pos;
                        while (pos < n && buf[pos] != ' ') pos++;
                        int ulen = pos - us2;
                        uid = 0;
                        for (int k = 0; k < ulen; k++)
                            uid = uid * 10 + ((char)buf[us2 + k] - '0');
                        break;
                    }
                }
                while (pos < n && buf[pos] != '\n') pos++;
                if (pos < n) pos++;
            }
            if (uid < 0) { Syscall.PrintString("No such user\r\n"); return; }

            while (i < len && line[i] == ' ') i++;
            int ps = i;
            while (i < len && line[i] != ' ') i++;
            int pl = i - ps;
            if (pl <= 0) { Syscall.PrintString("Usage: CHOWN <user> <path>\r\n"); return; }

            var pbuf = stackalloc byte[260];
            Narrow(line + ps, pbuf, pl);
            int rc = Syscall.Chown(pbuf, pl, (uint)uid);
            if (rc == 0) Syscall.PrintString("OK\r\n");
            else if (rc == -3) Syscall.PrintString("Permission denied\r\n");
            else Syscall.PrintString("Path not found\r\n");
        }

        private static void DoChmod(char* line, int len)
        {
            int i = 5;   // skip "CHMOD"
            while (i < len && line[i] == ' ') i++;
            if (i < len && line[i] == '/')
            {
                i++;
                if (i < len && line[i] == '?')
                {
                    Syscall.PrintString("CHMOD <octal> <path> [change file mode, owner or root]\r\n");
                    Syscall.PrintString("/? [show this help]\r\n");
                    return;
                }
            }
            int ms = i;
            while (i < len && line[i] != ' ') i++;
            int ml = i - ms;
            if (ml <= 0) { Syscall.PrintString("Usage: CHMOD <octal> <path>\r\n"); return; }

            ushort bits = 0;
            for (int k = 0; k < ml; k++)
            {
                char c = line[ms + k];
                if (c < '0' || c > '7') { Syscall.PrintString("Invalid mode (octal digits)\r\n"); return; }
                bits = (ushort)((bits << 3) | (c - '0'));
            }

            while (i < len && line[i] == ' ') i++;
            int ps = i;
            while (i < len && line[i] != ' ') i++;
            int pl = i - ps;
            if (pl <= 0) { Syscall.PrintString("Usage: CHMOD <octal> <path>\r\n"); return; }

            var pbuf = stackalloc byte[260];
            Narrow(line + ps, pbuf, pl);
            int rc = Syscall.Chmod(pbuf, pl, bits);
            if (rc == 0) Syscall.PrintString("OK\r\n");
            else if (rc == -3) Syscall.PrintString("Operation not permitted\r\n");
            else Syscall.PrintString("Path not found\r\n");
        }

        // ---- slice 6: jobs / processes ----

        private static void DoTask(char* line, int len)
        {
            int i = 4;
            while (i < len && line[i] == ' ') i++;
            if (i < len && line[i] == '/')
            {
                i++;
                if (i < len && line[i] == '?')
                {
                    Syscall.PrintString("TASK [&] [list tasks or run in background]\r\n");
                    Syscall.PrintString("/? [show this help]\r\n");
                    return;
                }
            }
            bool background = false;
            if (background)
            {
                // background listing: show tasks then return
                ListTasks();
                return;
            }
            ListTasks();
        }

        private static void DoKill(char* line, int len)
        {
            int i = 4;
            while (i < len && line[i] == ' ') i++;
            if (i < len && line[i] == '/')
            {
                i++;
                if (i < len && line[i] == '?')
                {
                    Syscall.PrintString("KILL <pid> [terminate a process]\r\n");
                    Syscall.PrintString("/? [show this help]\r\n");
                    return;
                }
            }
            if (i >= len) { Syscall.PrintString("Usage: WAIT <pid>\r\n"); return; }
            int pid = 0; bool any = false;
            while (i < len && line[i] >= '0' && line[i] <= '9') { pid = pid * 10 + (line[i] - '0'); i++; any = true; }
            if (!any) { Syscall.PrintString("Usage: KILL <pid>\r\n"); return; }
            if (Syscall.Kill(pid)) Syscall.PrintString("Killed pid ");
            else { Syscall.PrintString("No such process.\r\n"); return; }
            PrintULong((ulong)pid);
            Syscall.PutChar('.'); Syscall.PutChar('\r'); Syscall.PutChar('\n');
        }

        private static void DoWait(char* line, int len)
        {
            int i = 4;
            while (i < len && line[i] == ' ') i++;
            if (i < len && line[i] == '/')
            {
                i++;
                if (i < len && line[i] == '?')
                {
                    Syscall.PrintString("WAIT <pid> [wait for a process]\r\n");
                    Syscall.PrintString("/? [show this help]\r\n");
                    return;
                }
            }
            if (i >= len) { Syscall.PrintString("Usage: WAIT <pid>\r\n"); return; }
            int pid = 0; bool any = false;
            while (i < len && line[i] >= '0' && line[i] <= '9') { pid = pid * 10 + (line[i] - '0'); i++; any = true; }
            if (!any) { Syscall.PrintString("Usage: WAIT <pid>\r\n"); return; }
            Syscall.PrintString("Waiting for pid ");
            PrintULong((ulong)pid);
            Syscall.PutChar(' ');
            int code = Syscall.Wait(pid);
            if (code == -7)
            {
                Syscall.PrintString("...\r\nForeground job aborted (Ctrl+C).\r\n");
                return;
            }
            if (code < 0) { Syscall.PrintString("No such process (or already gone).\r\n"); return; }
            Syscall.PrintString("Process ");
            PrintULong((ulong)pid);
            Syscall.PrintString(" exited, code ");
            PrintULong((ulong)code);
            Syscall.PutChar('.'); Syscall.PutChar('\r'); Syscall.PutChar('\n');
        }

        private static void DoTab(char* line, int len)
        {
            int i = 3;
            while (i < len && line[i] == ' ') i++;
            if (i < len && line[i] == '/')
            {
                i++;
                if (i < len && line[i] == '?')
                {
                    Syscall.PrintString("TAB <pid> [set foreground process]\r\n");
                    Syscall.PrintString("/? [show this help]\r\n");
                    return;
                }
            }
            if (i >= len) { Syscall.PrintString("Usage: TAB <pid>\r\n"); return; }
            int pid = 0; bool any = false;
            while (i < len && line[i] >= '0' && line[i] <= '9') { pid = pid * 10 + (line[i] - '0'); i++; any = true; }
            if (!any) { Syscall.PrintString("Usage: TAB <pid>\r\n"); return; }
            if (Syscall.SetForeground(pid)) { Syscall.PrintString("Foreground [pid "); PrintULong((ulong)pid); Syscall.PrintString("]\r\n"); }
            else Syscall.PrintString("No such process.\r\n");
        }

        private static void DoSpawn(char* line, int len)
        {
            int i = 5;   // skip "SPAWN"
            while (i < len && line[i] == ' ') i++;
            if (i < len && line[i] == '/')
            {
                i++;
                if (i < len && line[i] == '?')
                {
                    Syscall.PrintString("SPAWN <program> [&] [run an external program]\r\n");
                    Syscall.PrintString("/? [show this help]\r\n");
                    return;
                }
            }
            bool background = false;
            if (i < len && line[i] == '&') { background = true; i++; }
            while (i < len && line[i] == ' ') i++;
            if (i >= len) { Syscall.PrintString("Usage: SPAWN <program> [&]\r\n"); return; }
            int pid = SpawnCmd(line + i, len - i);
            if (pid < 0)
            {
                Syscall.PrintString("Bad command or file name.\r\n");
                return;
            }
            if (background)
            {
                Syscall.PrintString("Started in background [pid ");
                PrintULong((ulong)pid);
                Syscall.PutChar(']'); Syscall.PutChar('\r'); Syscall.PutChar('\n');
                return;
            }
            Syscall.PrintString("Foreground [pid ");
            PrintULong((ulong)pid);
            Syscall.PrintString("]  (Ctrl+C returns to the shell)\r\n");
            int code = Syscall.Wait(pid);
            if (code == -7) { Syscall.PrintString("Foreground job aborted (Ctrl+C).\r\n"); return; }
            if (code >= 0) { Syscall.PrintString("Job finished, exit code "); PrintULong((ulong)code); Syscall.PutChar('.'); }
            else Syscall.PrintString("Job ended.");
            Syscall.PutChar('\r'); Syscall.PutChar('\n');
        }

        // ---- helpers ----

        private static void PrintULong(ulong v)
        {
            var buf = stackalloc char[24];
            int n = 0;
            if (v == 0) { PutCharBuf('0'); return; }
            while (v != 0) { buf[n++] = (char)('0' + (int)(v % 10)); v /= 10; }
            while (n > 0) PutCharBuf(buf[--n]);
        }

        private static void PrintTwo(int v)
        {
            if (v < 10) PutCharBuf('0');
            PrintULong((ulong)v);
        }

        private static void PutCharBuf(char c)
        {
            var b = stackalloc char[1]; b[0] = c;
            Syscall.Print(b, 1);
        }

        private static void ListTasks()
        {
            var buf = stackalloc byte[512];
            int n = Syscall.TaskList(buf, 512);
            int i = 0;
            bool any = false;
            while (i + 16 <= n)
            {
                int pid = buf[i] | (buf[i + 1] << 8) | (buf[i + 2] << 16) | (buf[i + 3] << 24);
                int state = buf[i + 4] | (buf[i + 5] << 8) | (buf[i + 6] << 16) | (buf[i + 7] << 24);
                any = true;
                PrintULong((ulong)pid);
                Syscall.PutChar(' ');
                for (int k = 0; k < 8; k++)
                {
                    byte ch = buf[i + 8 + k];
                    if (ch == 0) break;
                    Syscall.PutChar((char)ch);
                }
                Syscall.PutChar(' ');
                Syscall.PrintString(state == 1 ? "ready" : state == 2 ? "zombie" : "?");
                Syscall.PutChar('\r'); Syscall.PutChar('\n');
                i += 16;
            }
            if (!any) Syscall.PrintString("No tasks.\r\n");
        }
    }
}