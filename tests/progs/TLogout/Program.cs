using OsbKT;

namespace Osb.Tests
{
    // 29-logout-kills (1c, I5): boot program (session leader). Logs in as the
    // non-root "tester", spawns a background child (TSpin, loops forever), then
    // LOGOUT. Checks:
    //   Q2: the child is killed (WAIT -> -1); after LOGOUT every privileged call
    //       is refused (WHOAMI/SUDO/POWEROFF/REBOOT/spawn ROOTONLY == -3); a
    //       fresh LOGIN still works.
    //   Q5: a re-LOGIN as another user ("sudoer") tears down this session's
    //       tasks before authenticating (the freshly spawned child is gone).
    // The leader survives to print the result; kernel tasks (session 0) are
    // never touched. Prints "LOGOUT: PASS".
    internal static unsafe class Program
    {
        private static int Fill(byte* dst, string s, int cap)
        {
            int n = 0;
            fixed (char* p = s) { for (; p[n] != 0 && n < cap; n++) dst[n] = (byte)p[n]; }
            return n;
        }

        private static int Spawn(string path)
        {
            var pbuf = stackalloc byte[260];
            int plen = Fill(pbuf, path, 260);
            var req = new Syscall.SpawnReq
            {
                PathPtr = (ulong)pbuf, PathLen = (uint)plen,
                ArgsPtr = 0, ArgsLen = 0, StdinHandle = -1, StdoutHandle = -1
            };
            return Syscall.SpawnPath(&req);
        }

        private static void Main()
        {
            var name = stackalloc byte[16]; int nl = Fill(name, "tester", 16);
            var pass = stackalloc byte[16]; int pl = Fill(pass, "pw", 16);
            if (Syscall.Login(name, nl, pass, pl) != 0) { Fail("login"); }

            int tpid = Spawn("A:\\OSB\\BIN\\TSpin.EXE");
            if (tpid < 0) { Fail("spawn"); }

            int r = Syscall.Logout();
            // The background child must be gone (WAIT -> -1); we are still here.
            bool killed = Syscall.Wait(tpid) == -1;

            // Q2: after LOGOUT nothing privileged is allowed.
            var buf = stackalloc byte[16];
            bool after = Syscall.WhoAmI(buf, 16) == -3
                      && Syscall.Sudo() == -3
                      && Syscall.Raw(0x58, 0, 0, 0, 0, 0) == -3
                      && Syscall.Raw(0x59, 0, 0, 0, 0, 0) == -3
                      && Spawn("A:\\OSB\\BIN\\ROOTONLY.EXE") == -3;

            // Q2: a fresh LOGIN works.
            bool relogin = Syscall.Login(name, nl, pass, pl) == 0;

            // Q5: a re-LOGIN as another user kills this session's tasks.
            int t2 = Spawn("A:\\OSB\\BIN\\TSpin.EXE");
            if (t2 < 0) { Fail("spawn2"); }
            var sname = stackalloc byte[16]; int sn = Fill(sname, "sudoer", 16);
            bool reloginKill = Syscall.Login(sname, sn, pass, pl) == 0
                            && Syscall.Wait(t2) == -1;
            bool whoSudo = Syscall.WhoAmI(buf, 16) == 6 &&
                           buf[0] == 's' && buf[1] == 'u' && buf[2] == 'd' &&
                           buf[3] == 'o' && buf[4] == 'e' && buf[5] == 'r';

            if (r == 0 && killed && after && relogin && reloginKill && whoSudo)
            {
                Syscall.PrintString("LOGOUT: PASS\r\n");
                Syscall.Exit(0);
            }
            Fail(!after ? "notdenied" : !relogin ? "relogin" : !reloginKill ? "rekill" : "logout");
        }

        private static void Fail(string name)
        {
            Syscall.PrintString("LOGOUT: FAIL ");
            Syscall.PrintString(name);
            Syscall.PrintString("\r\n");
            Syscall.Exit(1);
        }
    }
}