using OsbKT;

namespace Osb.Tests
{
    // 30-sudo-scope (1c, I5): boot program (session leader). Log in as the
    // non-root sudoer "sudoer", elevate with SUDO, and check:
    //   #1 a child spawned while elevated inherits uid 0 (it can spawn the
    //      root-owned 0700 ROOTONLY.EXE),
    //   #2 END_SUDO restores the caller (it can no longer spawn ROOTONLY).
    // Elevation is per-task (the child keeps its inherited uid 0 even after the
    // parent de-elevates); it is dropped automatically when the elevated task
    // exits (its slot is freed).
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

            var name = stackalloc byte[16]; int nl = Fill(name, "sudoer", 16);
            var pass = stackalloc byte[16]; int pl = Fill(pass, "pw", 16);
            if (Syscall.Login(name, nl, pass, pl) != 0) { Fail("login"); }

            if (Syscall.Sudo() != 0) { Fail("sudo"); }

            // #1: the child inherits uid 0 (it can spawn the 0700 root file).
            int cpid = Spawn("A:\\OSB\\BIN\\TSudo.EXE", "CHILD");
            bool childElevated = cpid >= 0 && Syscall.Wait(cpid) == 0;

            // #2: END_SUDO restores the caller (no longer root).
            if (Syscall.EndSudo() != 0) { Fail("endsudo"); }
            int rpid = Spawn("A:\\OSB\\BIN\\ROOTONLY.EXE", null);
            bool parentRestored = rpid < 0;   // -3: not root anymore
            if (rpid >= 0) Syscall.Wait(rpid);

            if (childElevated && parentRestored)
            {
                Syscall.PrintString("TSUDO: PASS\r\n");
                Syscall.Exit(0);
            }
            Fail(!childElevated ? "childuid" : "endsudo");
        }

        // Child mode: elevated (inherited from the parent). It must be able to
        // spawn the root-owned 0700 file; exit 0 if so.
        private static void Child()
        {
            int r = Spawn("A:\\OSB\\BIN\\ROOTONLY.EXE", null);
            if (r >= 0) Syscall.Wait(r);
            Syscall.Exit(r >= 0 ? 0 : 1);
        }

        private static void Fail(string name)
        {
            Syscall.PrintString("TSUDO: FAIL ");
            Syscall.PrintString(name);
            Syscall.PrintString("\r\n");
            Syscall.Exit(1);
        }
    }
}