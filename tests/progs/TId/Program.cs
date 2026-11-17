using OsbKT;

namespace Osb.Tests
{
    // 28-identity (1c, I5): run as the seeded non-root user "tester" and check
    // every authentication/authorization failure path. Boot program (session
    // leader), so LOGIN is allowed. The summary is "TID: PASS n=10" (all ten)
    // or "TID: FAIL <name>".
    internal static unsafe class Program
    {
        private static int Fill(byte* dst, string s, int cap)
        {
            int n = 0;
            fixed (char* p = s) { for (; p[n] != 0 && n < cap; n++) dst[n] = (byte)p[n]; }
            return n;
        }

        private static int Spawn(string path, string args)
        {
            var pbuf = stackalloc byte[260];
            int plen = Fill(pbuf, path, 260);
            var abuf = stackalloc byte[128];
            int alen = args != null ? Fill(abuf, args, 128) : 0;
            var req = new Syscall.SpawnReq
            {
                PathPtr = (ulong)pbuf, PathLen = (uint)plen,
                ArgsPtr = alen > 0 ? (ulong)abuf : 0, ArgsLen = (uint)alen,
                StdinHandle = -1, StdoutHandle = -1
            };
            return Syscall.SpawnPath(&req);
        }

        private static void Main()
        {
            var args = stackalloc byte[128];
            int alen = Syscall.GetArgs(args, 128);
            if (alen >= 5 && args[0] == 'C' && args[1] == 'H' && args[2] == 'I' &&
                args[3] == 'L' && args[4] == 'D') { Child(); return; }

            var name = stackalloc byte[16]; int nl = Fill(name, "tester", 16);
            var pass = stackalloc byte[16]; int pl = Fill(pass, "pw", 16);
            var bad = stackalloc byte[16]; int bl = Fill(bad, "wrong", 16);
            var nobody = stackalloc byte[16]; int nb = Fill(nobody, "nobody", 16);
            var x = stackalloc byte[4]; x[0] = (byte)'x';
            // Q3: a valid 15-char name is seeded ("abcdefghijklmno"); appending
            // "QQ" makes 17 chars, which must be REJECTED, not truncated to
            // 15 and matched.
            var longname = stackalloc byte[20]; int ln = Fill(longname, "abcdefghijklmnoQQ", 20);

            int n = 0;
            // The failing LOGIN checks run FIRST, while NOT logged in: a LOGIN
            // while a session is active tears it down first (Q5), so we must not
            // let a rejected LOGIN disturb the session we use for the privileged
            // checks below.
            // 1. bad password -> -3 (same code as unknown user).
            if (Syscall.Login(name, nl, bad, bl) == -3) n++; else Fail("badpass");
            // 2. unknown user -> -3 (indistinguishable from a wrong password).
            if (Syscall.Login(nobody, nb, x, 1) == -3) n++; else Fail("unknown");
            // 3. Q3: an empty name is rejected outright (-3).
            if (Syscall.Login(longname, 0, pass, pl) == -3) n++; else Fail("emptyname");
            // 4. Q3: an over-long name (>= NameCap) is rejected, not truncated.
            if (Syscall.Login(longname, ln, pass, pl) == -3) n++; else Fail("longname");

            // 5. LOGIN tester/pw -> 0 (the user exists and the password is right).
            if (Syscall.Login(name, nl, pass, pl) == 0) n++; else Fail("login");
            // 6. SUDO as a non-sudoer -> -3.
            if (Syscall.Sudo() == -3) n++; else Fail("sudo");
            // 7. POWEROFF as non-root -> -3 (never test the root path).
            if (Syscall.Raw(0x58, 0, 0, 0, 0, 0) == -3) n++; else Fail("poweroff");
            // 8. REBOOT as non-root -> -3.
            if (Syscall.Raw(0x59, 0, 0, 0, 0, 0) == -3) n++; else Fail("reboot");
            // 9. spawn a root-owned 0700 file -> -3, no task created.
            if (Spawn("A:\\OSB\\BIN\\ROOTONLY.EXE", null) == -3) n++; else Fail("rootonly");
            // 10. a child spawned by us inherits the non-root uid and cannot
            //     LOGIN/LOGOUT (not the leader); its WHOAMI is still "tester".
            int cpid = Spawn("A:\\OSB\\BIN\\TId.EXE", "CHILD");
            if (cpid >= 0 && Syscall.Wait(cpid) == 0) n++; else Fail("childuid");

            Syscall.PrintString("TID: ");
            if (n == 10) { Syscall.PrintString("PASS n=10\r\n"); Syscall.Exit(0); }
            else { Syscall.PrintString("FAIL n="); PrintNum((uint)n); Syscall.PrintString("\r\n"); Syscall.Exit(1); }
        }

        // Child mode (Q1): not the session leader, so LOGIN and LOGOUT must both
        // be refused with -3; WHOAMI must still be "tester" afterwards.
        private static void Child()
        {
            var root = stackalloc byte[8]; int rn = Fill(root, "root", 8);
            var empty = stackalloc byte[4];
            bool loginDenied = Syscall.Login(root, rn, empty, 0) == -3;
            bool logoutDenied = Syscall.Logout() == -3;

            var buf = stackalloc byte[16];
            int r = Syscall.WhoAmI(buf, 16);
            bool ok = loginDenied && logoutDenied && r == 6 &&
                      buf[0] == 't' && buf[1] == 'e' && buf[2] == 's' &&
                      buf[3] == 't' && buf[4] == 'e' && buf[5] == 'r';
            Syscall.Exit(ok ? 0 : 1);
        }

        private static void Fail(string name)
        {
            Syscall.PrintString("TID: FAIL ");
            Syscall.PrintString(name);
            Syscall.PrintString("\r\n");
            Syscall.Exit(1);
        }

        private static void PrintNum(uint v)
        {
            var buf = stackalloc char[16];
            int n = Fmt.U32(buf, v);
            Syscall.Print(buf, n);
        }
    }
}