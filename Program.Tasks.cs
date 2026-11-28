using OsbKT;

namespace Osb.Shell.Userspace
{
    internal static unsafe partial class Program
    {
        // ---- slice 6: jobs / processes (Card 8: PS / KILL) ----
        //
        // Process identity is the Kernel-owned 64-bit PID (Card 3). These
        // commands never touch Kernel data structures directly: PS reads a
        // snapshot through the TASKLIST syscall and KILL requests termination
        // through the KILL syscall. The Kernel enforces authorization and
        // lifecycle; the shell only parses, formats, and explains the result.

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
            if (i >= len) { Syscall.PrintString("Usage: KILL <pid>\r\n"); return; }
            if (!ParsePid(line, len, i, out ulong pid))
            {
                Syscall.PrintString("Invalid PID.\r\n");
                return;
            }
            // Request termination through the authorized Kernel interface. The
            // Kernel decides whether the caller may kill the target and whether
            // it exists; we only report the outcome (never claim success early).
            int r = Syscall.KillStatus(pid);
            if (r == 0)
            {
                Syscall.PrintString("Killed pid ");
                PrintULong(pid);
                Syscall.PutChar('.'); Syscall.PutChar('\r'); Syscall.PutChar('\n');
                return;
            }
            if (r == -3)
            {
                // EACCES: console (pid 1) or a process the caller may not signal.
                Syscall.PrintString("Operation not permitted.\r\n");
                return;
            }
            // -4 (or anything else): unknown or already-terminated target.
            Syscall.PrintString("No such process.\r\n");
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
            if (!ParsePid(line, len, i, out ulong pid))
            {
                Syscall.PrintString("Invalid PID.\r\n");
                return;
            }
            Syscall.PrintString("Waiting for pid ");
            PrintULong(pid);
            Syscall.PutChar(' ');
            int code = Syscall.Wait(pid);
            if (code == -7)
            {
                Syscall.PrintString("...\r\nForeground job aborted (Ctrl+C).\r\n");
                return;
            }
            if (code < 0) { Syscall.PrintString("No such process (or already gone).\r\n"); return; }
            Syscall.PrintString("Process ");
            PrintULong(pid);
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
            if (!ParsePid(line, len, i, out ulong pid))
            {
                Syscall.PrintString("Invalid PID.\r\n");
                return;
            }
            if (Syscall.SetForeground(pid)) { Syscall.PrintString("Foreground [pid "); PrintULong(pid); Syscall.PrintString("]\r\n"); }
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

        // Parse a base-10 unsigned 64-bit PID starting at `start`. Rejects an
        // empty field, any non-digit trailing character, and values that
        // overflow ulong (out-of-range). The whole remainder must be the number.
        private static bool ParsePid(char* line, int len, int start, out ulong pid)
        {
            pid = 0;
            int i = start;
            if (i >= len) return false;
            bool any = false;
            for (; i < len; i++)
            {
                char c = line[i];
                if (c < '0' || c > '9') return false;      // malformed (trailing junk / sign)
                int d = c - '0';
                if (pid > (ulong.MaxValue - (ulong)d) / 10UL) return false; // overflow
                pid = pid * 10UL + (ulong)d;
                any = true;
            }
            return any;
        }

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

        // PS: list the processes the Kernel reports via TASKLIST. Each record
        // is 20 bytes: pid u64, state u32, name[8]. The list is Kernel-owned —
        // this is a snapshot, not a scan of any local structure.
        private static void ListTasks()
        {
            var buf = stackalloc byte[512];
            // TASKLIST returns the number of records written (not a byte count).
            int count = Syscall.TaskList(buf, 512);
            int i = 0;
            for (int rec = 0; rec < count && i + 20 <= 512; rec++)
            {
                ulong pid = 0;
                for (int b = 0; b < 8; b++) pid |= ((ulong)buf[i + b]) << (b * 8);
                int state = buf[i + 8] | (buf[i + 9] << 8) | (buf[i + 10] << 16) | (buf[i + 11] << 24);
                PrintULong(pid);
                Syscall.PutChar(' ');
                for (int k = 0; k < 8; k++)
                {
                    byte ch = buf[i + 12 + k];
                    if (ch == 0) break;
                    Syscall.PutChar((char)ch);
                }
                Syscall.PutChar(' ');
                Syscall.PrintString(StateName(state));
                Syscall.PutChar('\r'); Syscall.PutChar('\n');
                i += 20;
            }
            if (count <= 0) Syscall.PrintString("No tasks.\r\n");
        }

        // The Kernel process-state model (Memory/Layout.cs): 0 Free (empty slot,
        // never listed), 1 Ready (runnable or running), 2 Zombie (exited, not yet
        // reaped). There are no distinct Stopped/Halted/Blocked states, so none
        // are invented here.
        private static string StateName(int state)
        {
            if (state == 1) return "ready";
            if (state == 2) return "zombie";
            return "?";
        }
    }
}