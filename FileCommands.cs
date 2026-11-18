using OsbKT;

namespace Osb.Shell.Userspace
{
    // Slice 4 filesystem read commands.
    internal static unsafe class FileCommands
    {
        private const int NameCap = 256;
        private const int BufCap = 512;

        public static void DoMd(char* line, int len)
        {
            int i = 2;
            while (i < len && line[i] == ' ') i++;
            if (i < len && line[i] == '/')
            {
                i++;
                if (i < len && line[i] == '?')
                {
                    Syscall.PrintString("MD <dir> [create directory]\r\n");
                    Syscall.PrintString("/? [show this help]\r\n");
                    return;
                }
            }
            if (i >= len) { Syscall.PrintString("Usage: MD <dir>\r\n"); return; }
            var path = stackalloc byte[256];
            int n = 0;
            for (int k = i; k < len && n < 255; k++) path[n++] = (byte)line[k];
            int r = Syscall.Mkdir(path, n);
            if (r != 0) Syscall.PrintString("Error\r\n");
        }

        public static void DoRd(char* line, int len)
        {
            int i = 2;
            while (i < len && line[i] == ' ') i++;
            if (i < len && line[i] == '/')
            {
                i++;
                if (i < len && line[i] == '?')
                {
                    Syscall.PrintString("RD <dir> [remove directory]\r\n");
                    Syscall.PrintString("/? [show this help]\r\n");
                    return;
                }
            }
            if (i >= len) { Syscall.PrintString("Usage: RD <dir>\r\n"); return; }
            var path = stackalloc byte[256];
            int n = 0;
            for (int k = i; k < len && n < 255; k++) path[n++] = (byte)line[k];
            int r = Syscall.Rmdir(path, n);
            if (r != 0) Syscall.PrintString("Error\r\n");
            else Syscall.PrintString("OK\r\n");
        }

        public static void DoDel(char* line, int len)
        {
            int i = 3;
            while (i < len && line[i] == ' ') i++;
            if (i < len && line[i] == '/')
            {
                i++;
                if (i < len && line[i] == '?')
                {
                    Syscall.PrintString("DEL <file> [delete file]\r\n");
                    Syscall.PrintString("/? [show this help]\r\n");
                    return;
                }
            }
            if (i >= len) { Syscall.PrintString("Usage: DEL <file>\r\n"); return; }
            // The kernel's delete syscall (0x27) expects "DEL <filename>" —
            // it skips the command-name token before parsing the file path.
            var buf = stackalloc byte[256];
            int n = 0;
            buf[n++] = (byte)'D'; buf[n++] = (byte)'E'; buf[n++] = (byte)'L'; buf[n++] = (byte)' ';
            for (int k = i; k < len && n < 255; k++) buf[n++] = (byte)line[k];
            Syscall.Delete(buf, n);
        }

        public static void DoRen(char* line, int len)
        {
            int i = 3;
            while (i < len && line[i] == ' ') i++;
            if (i < len && line[i] == '/')
            {
                i++;
                if (i < len && line[i] == '?')
                {
                    Syscall.PrintString("REN old new [rename file]\r\n");
                    Syscall.PrintString("/? [show this help]\r\n");
                    return;
                }
            }
            int s1 = i;
            while (i < len && line[i] != ' ') i++;
            int l1 = i - s1;
            if (l1 <= 0) { Syscall.PrintString("Usage: REN old new\r\n"); return; }
            while (i < len && line[i] == ' ') i++;
            int s2 = i;
            while (i < len && line[i] != ' ') i++;
            int l2 = i - s2;
            if (l2 <= 0) { Syscall.PrintString("Usage: REN old new\r\n"); return; }
            var p1 = stackalloc byte[256];
            var p2 = stackalloc byte[256];
            int n1 = 0, n2 = 0;
            for (int k = s1; k < s1 + l1 && n1 < 255; k++) p1[n1++] = (byte)line[k];
            for (int k = s2; k < s2 + l2 && n2 < 255; k++) p2[n2++] = (byte)line[k];
            Syscall.Rename(p1, n1, p2, n2);
        }

        public static void DoCopy(char* line, int len)
        {
            int i = 4;
            while (i < len && line[i] == ' ') i++;
            if (i < len && line[i] == '/')
            {
                i++;
                if (i < len && line[i] == '?')
                {
                    Syscall.PrintString("COPY src dst [copy file]\r\n");
                    Syscall.PrintString("/? [show this help]\r\n");
                    return;
                }
            }
            int s1 = i;
            while (i < len && line[i] != ' ') i++;
            int l1 = i - s1;
            if (l1 <= 0) { Syscall.PrintString("Usage: COPY src dst\r\n"); return; }
            while (i < len && line[i] == ' ') i++;
            int s2 = i;
            while (i < len && line[i] != ' ') i++;
            int l2 = i - s2;
            if (l2 <= 0) { Syscall.PrintString("Usage: COPY src dst\r\n"); return; }
            var p1 = stackalloc byte[256];
            var p2 = stackalloc byte[256];
            int n1 = 0, n2 = 0;
            for (int k = s1; k < s1 + l1 && n1 < 255; k++) p1[n1++] = (byte)line[k];
            for (int k = s2; k < s2 + l2 && n2 < 255; k++) p2[n2++] = (byte)line[k];
            Syscall.Copy(p1, n1, p2, n2);
        }

        public static void DoLn(char* line, int len)
        {
            int i = 2;
            while (i < len && line[i] == ' ') i++;
            if (i < len && line[i] == '/')
            {
                i++;
                if (i < len && line[i] == '?')
                {
                    Syscall.PrintString("LN <link> <target> [create link]\r\n");
                    Syscall.PrintString("/? [show this help]\r\n");
                    return;
                }
            }
            var buf = stackalloc byte[256];
            int n = 0;
            for (int k = 0; k < len && n < 255; k++) buf[n++] = (byte)line[k];
            Syscall.Ln(buf, n);
        }

        public static void DoTrash(char* line, int len)
        {
            int i = 5;
            while (i < len && line[i] == ' ') i++;
            if (i < len && line[i] == '/')
            {
                i++;
                if (i < len && line[i] == '?')
                {
                    Syscall.PrintString("TRASH [/F pattern] [/U name] [manage trash]\r\n");
                    Syscall.PrintString("/? [show this help]\r\n");
                    return;
                }
            }
            var buf = stackalloc byte[256];
            int n = 0;
            for (int k = 0; k < len && n < 255; k++) buf[n++] = (byte)line[k];
            Syscall.Trash(buf, n);
        }

        public static void DoPwd(char* line, int len)
        {
            int i = 3;   // skip "PWD"
            while (i < len && line[i] == ' ') i++;
            if (i < len && line[i] == '/')
            {
                i++;
                if (i < len && line[i] == '?')
                {
                    Syscall.PrintString("PWD [print working directory]\r\n");
                    Syscall.PrintString("/? [show this help]\r\n");
                    return;
                }
            }
            var buf = stackalloc byte[256];
            int n = Syscall.GetCwd(buf, 256);
            if (n < 0) n = 0;
            Syscall.Write(buf, n);
            Syscall.PutChar('\r'); Syscall.PutChar('\n');
        }

        public static void DoCd(char* line, int len)
        {
            int i = 2;
            while (i < len && line[i] == ' ') i++;
            if (i < len && line[i] == '/')
            {
                i++;
                if (i < len && line[i] == '?')
                {
                    Syscall.PrintString("CD [path] [change directory]\r\n");
                    Syscall.PrintString("/? [show this help]\r\n");
                    return;
                }
            }
            if (i >= len)
            {
                DoPwd(line, len);
                return;
            }
            var path = stackalloc byte[256];
            int n = 0;
            for (int k = i; k < len && n < 255; k++) path[n++] = (byte)line[k];
            if (Syscall.ChDir(path, n) != 0)
                Syscall.PrintString("Path not found\r\n");
            else
                Syscall.PrintString("OK\r\n");
        }

        public static void DoSize(char* line, int len)
        {
            int i = 4;   // skip "SIZE"
            while (i < len && line[i] == ' ') i++;
            if (i < len && line[i] == '/')
            {
                i++;
                if (i < len && line[i] == '?')
                {
                    Syscall.PrintString("SIZE <file> [show file size]\r\n");
                    Syscall.PrintString("/? [show this help]\r\n");
                    return;
                }
            }
            if (i >= len) { Syscall.PrintString("Usage: SIZE <file>\r\n"); return; }
            var path = stackalloc byte[256];
            int n = 0;
            for (int k = i; k < len && n < 255; k++) path[n++] = (byte)line[k];
            uint ino; ushort mode, uid; ulong size;
            if (!Syscall.ResolvePath(path, n, out ino, out mode, out uid, out size))
            {
                Syscall.PrintString("File not found\r\n");
                return;
            }
            if ((mode & 0x4000) != 0) { Syscall.PrintString("File not found\r\n"); return; }
            // print size
            var num = stackalloc char[16];
            int nl = Fmt.U32(num, (uint)size);
            Syscall.Print(num, nl);
            Syscall.PutChar('\r'); Syscall.PutChar('\n');
        }

        public static void DoType(char* line, int len)
        {
            int i = 4;   // skip "TYPE"
            while (i < len && line[i] == ' ') i++;
            if (i < len && line[i] == '/')
            {
                i++;
                if (i < len && line[i] == '?')
                {
                    Syscall.PrintString("TYPE <file> [display file contents]\r\n");
                    Syscall.PrintString("/? [show this help]\r\n");
                    return;
                }
            }
            if (i >= len) { Syscall.PrintString("Usage: TYPE <file>\r\n"); return; }
            var path = stackalloc byte[256];
            int n = 0;
            for (int k = i; k < len && n < 255; k++) path[n++] = (byte)line[k];
            int h = Syscall.Open(path, n);
            if (h < 0)
            {
                if (h == -3) Syscall.PrintString("Permission denied\r\n");
                else Syscall.PrintString("File not found\r\n");
                return;
            }
            var buf = stackalloc byte[512];
            for (;;)
            {
                int got = Syscall.Read(h, buf, 512);
                if (got <= 0) break;
                Syscall.Write(buf, got);
            }
            Syscall.Close(h);
            Syscall.PutChar('\r'); Syscall.PutChar('\n');
        }

        public static void DoDir(char* line, int len)
        {
            // DIR [path] [/W] [/?] — long form shows type, permissions, owner, size
            bool wide = false;
            bool help = false;
            int i = 3;   // skip "DIR"
            while (i < len && line[i] == ' ') i++;
            var path = stackalloc byte[256];
            int pn = 0;
            for (int k = i; k < len; k++)
            {
                if (line[k] == '/' && k + 1 < len)
                {
                    if (line[k + 1] == 'W' || line[k + 1] == 'w')
                    {
                        wide = true;
                        k++; continue;
                    }
                    if (line[k + 1] == '?')
                    {
                        help = true;
                        k++; continue;
                    }
                }
                if (line[k] != ' ' && pn < 255) path[pn++] = (byte)line[k];
            }
            if (help)
            {
                Syscall.PrintString("DIR [path] [/W] [list directory contents]\r\n");
                Syscall.PrintString("/W [wide format]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }
            if (pn == 0)
            {
                var cwd = stackalloc byte[256];
                int cl = Syscall.GetCwd(cwd, 256);
                if (cl < 0) cl = 0;
                for (int k = 0; k < cl && pn < 255; k++) path[pn++] = cwd[k];
            }
            uint ino; ushort mode, uid; ulong size;
            if (!Syscall.ResolvePath(path, pn, out ino, out mode, out uid, out size))
            {
                Syscall.PrintString("Path not found\r\n");
                return;
            }
            if ((mode & 0x4000) == 0)
            {
                Syscall.PrintString("Path not found\r\n");
                return;
            }
            // ensure path ends with separator for child resolution
            if (pn > 0 && path[pn - 1] != '\\' && path[pn - 1] != '/' && pn < 255)
                path[pn++] = (byte)'\\';

            var ebuf = stackalloc byte[256];
            int idx = 0;
            bool any = false;
            for (;;)
            {
                bool r = Syscall.DirNext(ino, idx, ebuf, 256);
                if (!r) break;
                uint cino = (uint)(ebuf[0] | (ebuf[1] << 8) | (ebuf[2] << 16) | (ebuf[3] << 24));
                byte type = ebuf[4];
                byte nlen = ebuf[5];

                // build child path: parent path + name
                int cpn = pn;
                for (int k = 0; k < nlen && cpn < 255; k++) path[cpn++] = ebuf[6 + k];

                ushort cmode; ushort cuid; ulong csize;
                if (Syscall.ResolvePath(path, cpn, out cino, out cmode, out cuid, out csize))
                {
                    any = true;
                    if (wide)
                    {
                        for (int k = 0; k < nlen; k++) Syscall.PutChar((char)ebuf[6 + k]);
                        Syscall.PutChar(' ');
                    }
                    else
                    {
                        Syscall.PutChar((cmode & 0x4000) != 0 ? (char)'d' : (char)'-');
                        Syscall.PutChar((cmode & 0x100) != 0 ? (char)'r' : (char)'-');
                        Syscall.PutChar((cmode & 0x080) != 0 ? (char)'w' : (char)'-');
                        Syscall.PutChar((cmode & 0x040) != 0 ? (char)'x' : (char)'-');
                        Syscall.PutChar((cmode & 0x020) != 0 ? (char)'r' : (char)'-');
                        Syscall.PutChar((cmode & 0x010) != 0 ? (char)'w' : (char)'-');
                        Syscall.PutChar((cmode & 0x008) != 0 ? (char)'x' : (char)'-');
                        Syscall.PutChar((cmode & 0x004) != 0 ? (char)'r' : (char)'-');
                        Syscall.PutChar((cmode & 0x002) != 0 ? (char)'w' : (char)'-');
                        Syscall.PutChar((cmode & 0x001) != 0 ? (char)'x' : (char)'-');
                        Syscall.PutChar(' ');
                        PrintUInt((ulong)cuid);
                        Syscall.PutChar(' ');
                        PrintULong(csize);
                        Syscall.PutChar(' ');
                        for (int k = 0; k < nlen; k++) Syscall.PutChar((char)ebuf[6 + k]);
                        Syscall.PutChar('\r'); Syscall.PutChar('\n');
                    }
                }
                idx++;
            }
            if (!any && !wide) Syscall.PrintString("Empty.\r\n");
            if (wide) { Syscall.PutChar('\r'); Syscall.PutChar('\n'); }
        }

        private static void PrintUInt(ulong v)
        {
            var buf = stackalloc char[24];
            int n = 0;
            if (v == 0) { PutChar2('0'); return; }
            while (v != 0) { buf[n++] = (char)('0' + (int)(v % 10)); v /= 10; }
            while (n > 0) PutChar2(buf[--n]);
        }

        private static void PrintULong(ulong v)
        {
            var buf = stackalloc char[24];
            int n = 0;
            if (v == 0) { PutChar2('0'); return; }
            while (v != 0) { buf[n++] = (char)('0' + (int)(v % 10)); v /= 10; }
            while (n > 0) PutChar2(buf[--n]);
        }

        private static void PutChar2(char c)
        {
            var b = stackalloc char[1]; b[0] = c;
            Syscall.Print(b, 1);
        }
    }
}