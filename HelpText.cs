using System;
using OsbKT;

namespace Osb.Shell.Userspace
{
    internal static unsafe partial class Program
    {
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
                Syscall.PrintString("  TRASH USER LOGIN LOGOUT WHOAMI\r\n");
                Syscall.PrintString("  CHOWN CHMOD SUDO HISTORY TASK PS KILL WAIT TAB SPAWN\r\n");
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
            // Simple: print the same page as '/?' for known commands (minimal).
            Syscall.PrintString("No help for ");
            Syscall.Print(line + s, tl);
            Syscall.PrintString("\r\n");
        }


    }
}
