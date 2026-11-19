using OsbKT;

namespace Osb.Shell.Userspace
{
    internal static unsafe partial class Program
    {
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
