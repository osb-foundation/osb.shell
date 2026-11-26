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
            bool showCore = false;
            bool showToolkit = false;
            bool showOslang = false;
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
                                case 'C': showCore = true; break;
                                case 'T': showToolkit = true; break;
                                case 'L': showOslang = true; break;
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
                Syscall.PrintString("/C [show core version]\r\n");
                Syscall.PrintString("/T [show toolkit version]\r\n");
                Syscall.PrintString("/L [show OSLANG version]\r\n");
                Syscall.PrintString("/A [show about information]\r\n");
                Syscall.PrintString("/? [show this help]\r\n");
                return;
            }

            // Zero-allocation: stack-allocated backing storage for version
            // entries, valid for the entire VER operation. No heap-allocated
            // byte array; the buffer is passed as a pointer to the version
            // routines (see Program.Version.cs).
            var verStorage = stackalloc byte[MaxVersionEntries * VersionEntrySize];
            int verCount;
            bool verAvailable;
            LoadVersions(verStorage, out verCount, out verAvailable);

            if (!anyFlag)
            {
                // Default: show all versions
                char* kernelVer = stackalloc char[32]; int kvl;
                char* shellVer = stackalloc char[32]; int svl;
                char* coreVer = stackalloc char[32]; int cvl;
                char* toolkitVer = stackalloc char[32]; int tvl;
                char* oslangVer = stackalloc char[32]; int olvl;
                char* codename = stackalloc char[32]; int cl;

                fixed (char* ckernel = "osb.kernel")
                fixed (char* cshell = "osb.shell")
                fixed (char* ccore = "osb.core")
                fixed (char* ctoolkit = "osbkt")
                fixed (char* coslang = "osb.oslang")
                {
                    GetVersionString(ckernel, kernelVer, 32, out kvl, verStorage, verCount, verAvailable);
                    GetVersionString(cshell, shellVer, 32, out svl, verStorage, verCount, verAvailable);
                    GetVersionString(ccore, coreVer, 32, out cvl, verStorage, verCount, verAvailable);
                    GetVersionString(ctoolkit, toolkitVer, 32, out tvl, verStorage, verCount, verAvailable);
                    GetVersionString(coslang, oslangVer, 32, out olvl, verStorage, verCount, verAvailable);
                    GetCodename(codename, 32, out cl, verStorage, verCount, verAvailable);
                }

                // Print OSB version line
                Syscall.PrintString("OSB ");
                for (int k = 0; k < kvl; k++) Syscall.PutChar(kernelVer[k]);
                Syscall.PutChar(' ');
                for (int k = 0; k < cl; k++) Syscall.PutChar(codename[k]);
                Syscall.PrintString("\r\n");

                // Print component versions
                Syscall.PrintString("Kernel: ");
                for (int k = 0; k < kvl; k++) Syscall.PutChar(kernelVer[k]);
                Syscall.PrintString("\r\n");

                Syscall.PrintString("Shell: ");
                for (int k = 0; k < svl; k++) Syscall.PutChar(shellVer[k]);
                Syscall.PrintString("\r\n");

                Syscall.PrintString("Core: ");
                for (int k = 0; k < cvl; k++) Syscall.PutChar(coreVer[k]);
                Syscall.PrintString("\r\n");

                Syscall.PrintString("osbkt: ");
                for (int k = 0; k < tvl; k++) Syscall.PutChar(toolkitVer[k]);
                Syscall.PrintString("\r\n");

                Syscall.PrintString("OSLANG: ");
                for (int k = 0; k < olvl; k++) Syscall.PutChar(oslangVer[k]);
                Syscall.PrintString("\r\n");
            }
            else
            {
                if (showKernel)
                {
                    char* v = stackalloc char[32]; int vl;
                    fixed (char* c = "osb.kernel") GetVersionString(c, v, 32, out vl, verStorage, verCount, verAvailable);
                    Syscall.PrintString("Kernel: ");
                    for (int k = 0; k < vl; k++) Syscall.PutChar(v[k]);
                    Syscall.PrintString("\r\n");
                }
                if (showShell)
                {
                    char* v = stackalloc char[32]; int vl;
                    fixed (char* c = "osb.shell") GetVersionString(c, v, 32, out vl, verStorage, verCount, verAvailable);
                    Syscall.PrintString("Shell: ");
                    for (int k = 0; k < vl; k++) Syscall.PutChar(v[k]);
                    Syscall.PrintString("\r\n");
                }
                if (showCore)
                {
                    char* v = stackalloc char[32]; int vl;
                    fixed (char* c = "osb.core") GetVersionString(c, v, 32, out vl, verStorage, verCount, verAvailable);
                    Syscall.PrintString("Core: ");
                    for (int k = 0; k < vl; k++) Syscall.PutChar(v[k]);
                    Syscall.PrintString("\r\n");
                }
                if (showToolkit)
                {
                    char* v = stackalloc char[32]; int vl;
                    fixed (char* c = "osbkt") GetVersionString(c, v, 32, out vl, verStorage, verCount, verAvailable);
                    Syscall.PrintString("osbkt: ");
                    for (int k = 0; k < vl; k++) Syscall.PutChar(v[k]);
                    Syscall.PrintString("\r\n");
                }
                if (showOslang)
                {
                    char* v = stackalloc char[32]; int vl;
                    fixed (char* c = "osb.oslang") GetVersionString(c, v, 32, out vl, verStorage, verCount, verAvailable);
                    Syscall.PrintString("OSLANG: ");
                    for (int k = 0; k < vl; k++) Syscall.PutChar(v[k]);
                    Syscall.PrintString("\r\n");
                }
                if (showAbout)
                {
                    char* c = stackalloc char[32]; int cl;
                    GetCodename(c, 32, out cl, verStorage, verCount, verAvailable);
                    Syscall.PrintString("OSB: Operating System Basic, 2000-2026 - ");
                    for (int k = 0; k < cl; k++) Syscall.PutChar(c[k]);
                    Syscall.PrintString("\r\n");
                }
            }
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