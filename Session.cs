using System;
using OsbKT;

namespace Osb.Shell.Userspace
{
    internal static unsafe partial class Program
    {
        private static void Session()
        {
            var line = stackalloc char[LineCap];
            var prompt = stackalloc char[CwdCap + NameCap + 8];
            var rc = stackalloc char[History.LineCap];   // recall target (once)
            var expanded = stackalloc char[Env.ExpandCap];
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
                int elen = Env.Expand(line, len, expanded, Env.ExpandCap);
                if (elen > 0)
                {
                    Dispatch(expanded, elen);
                }
            }
        }

        // First command that matches the line's first token. EXIT exits;
        // LOGIN/LOGOUT re-enter Login() in place; SUDO runs one command as root.

    }
}
