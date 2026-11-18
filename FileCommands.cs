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
            if (i >= len) { Syscall.PrintString("Usage: DEL <file>\r\n"); return; }
            var path = stackalloc byte[256];
            int n = 0;
            for (int k = i; k < len && n < 255; k++) path[n++] = (byte)line[k];
            Syscall.Delete(path, n);
        }

        public static void DoRen(char* line, int len)
        {
            int i = 3;
            while (i < len && line[i] == ' ') i++;
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

        public static void DoPwd()
        {
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
            if (i >= len)
            {
                DoPwd();
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
            // DIR [path] [/W]
            bool wide = false;
            int i = 3;   // skip "DIR"
            while (i < len && line[i] == ' ') i++;
            // check for /W flag
            // simple: parse path token
            var path = stackalloc byte[256];
            int pn = 0;
            // find /W
            for (int k = i; k < len; k++)
            {
                if (line[k] == '/' && k + 1 < len && (line[k+1] == 'W' || line[k+1] == 'w'))
                {
                    wide = true;
                    // skip the flag
                    k++; continue;
                }
                if (line[k] != ' ') path[pn++] = (byte)line[k];
            }
            if (pn == 0)
            {
                // use cwd
                var cwd = stackalloc byte[256];
                int cl = Syscall.GetCwd(cwd, 256);
                if (cl < 0) cl = 0;
                for (int k = 0; k < cl; k++) path[pn++] = cwd[k];
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
            // iterate entries
            var ebuf = stackalloc byte[256];
            int idx = 0;
            for (;;)
            {
                bool r = Syscall.DirNext(ino, idx, ebuf, 256);
                if (!r) break;
                byte type = ebuf[4];
                byte nlen = ebuf[5];
                // print name
                for (int k = 0; k < nlen; k++) Syscall.PutChar((char)ebuf[6 + k]);
                if (!wide)
                {
                    // long form: size for files
                    if (type != 2)   // not directory
                    {
                        // get size via resolve of child? skip; print spaces
                        Syscall.PrintString("        ");
                    }
                    Syscall.PutChar('\r'); Syscall.PutChar('\n');
                }
                else
                {
                    Syscall.PutChar(' ');
                }
                idx++;
            }
            if (wide) { Syscall.PutChar('\r'); Syscall.PutChar('\n'); }
        }
    }
}