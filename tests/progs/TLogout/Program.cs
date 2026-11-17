using OsbKT;

namespace Osb.Tests
{
    // 29-logout-kills (1c, I5): boot program (session leader). Log in as the
    // non-root "tester", spawn a background child (TSpin, loops forever), then
    // LOGOUT. The child must be killed (WAIT returns -1); the leader survives to
    // print the result. Kernel tasks (session 0) are never touched.
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
            if (r == 0 && killed) { Syscall.PrintString("LOGOUT: PASS\r\n"); Syscall.Exit(0); }
            else Fail(r == 0 ? "notkilled" : "logout");
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