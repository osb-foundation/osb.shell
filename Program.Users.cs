using OsbKT;

namespace Osb.Shell.Userspace
{
    internal static unsafe partial class Program
    {
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
    }
}
