using OsbKT;

namespace Osb.Shell.Userspace
{
    internal static unsafe partial class Program
    {
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

            if (!anyFlag)
            {
                Syscall.PrintString("OSB 0.1.0 Olorun\r\n");
                Syscall.PrintString("Kernel: 0.1.0\r\n");
                Syscall.PrintString("Shell: 0.1.0\r\n");
                Syscall.PrintString("Core: 0.1.0\r\n");
                Syscall.PrintString("osbkt: 0.1.0\r\n");
                Syscall.PrintString("OSLANG: 0.8.0\r\n");
            }
            if (showKernel)
                Syscall.PrintString("OSB (dotnet) version 0.1.0 - bootable kernel (Olorun)\r\n");
            if (showShell)
                Syscall.PrintString("OSB shell version 0.1.0 (Olorun)\r\n");
            if (showAbout)
                Syscall.PrintString("OSB: Operating System Basic, 2000-2026 - Olorun\r\n");
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
    }
}
