using System;
using OsbKT;

namespace Osb.Shell.Userspace
{
    internal static unsafe partial class Program
    {
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


    }
}
