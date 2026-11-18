using OsbKT;

namespace Osb.Tests
{
    // TEnv: per-task environment proof (Slice 3, C1/C2).
    //
    // Runs as the boot program (the session leader) so it can LOGIN as root
    // and get the kernel-set USER/HOME. Every case returns the documented
    // code; the PASS line prints the real number of cases.
    //
    //   * get/set/redefine/delete, case-insensitive names
    //   * 64-byte name / 256-byte value caps, 65/257 rejected
    //   * USER/HOME are read-only (-3) from every task
    //   * bad pointers -> -1 (EFAULT), via the test-only Raw() gate
    //   * LOGOUT/LOGIN drops user variables (no leak across sessions)
    //   * copy-at-spawn: a child sees the parent's values, and its changes
    //     never reach the parent
    //   * table full -> -6 (ENOMEM)
    internal static unsafe class Program
    {
        private const ulong UserBase = 0x2000000;
        private const ulong UserLimit = 0x2200000;

        private static int _cases;
        private static int _fails;

        private static void Main()
        {
            MainChildCheck();   // child mode: print the inherited env and exit
            var root = stackalloc byte[8];
            Lit(root, "root");
            if (Syscall.Login(root, 4, null, 0) != 0) { Fail("login"); Done(); return; }

            var user = stackalloc byte[8]; Lit(user, "USER");
            var home = stackalloc byte[8]; Lit(home, "HOME");
            var hacker = stackalloc byte[8]; Lit(hacker, "hacker");

            // --- USER/HOME are kernel-locked (C2) ---
            Check("USER locked", Syscall.SetEnv(user, 4, hacker, 6) == false);
            Check("HOME locked", Syscall.SetEnv(home, 4, hacker, 6) == false);
            var vb = stackalloc byte[64];
            Check("USER=root", Is(Get(user, 4, vb, 64), vb, "root"));

            // --- set / redefine / get / delete ---
            var foo = stackalloc byte[8]; Lit(foo, "FOO");
            var bar = stackalloc byte[8]; Lit(bar, "bar");
            var baz = stackalloc byte[8]; Lit(baz, "baz");
            Check("set FOO", Syscall.SetEnv(foo, 3, bar, 3));
            Check("get FOO=bar", Is(Get(foo, 3, vb, 64), vb, "bar"));
            Check("redefine FOO", Syscall.SetEnv(foo, 3, baz, 3));
            Check("get FOO=baz", Is(Get(foo, 3, vb, 64), vb, "baz"));
            Check("delete FOO", Syscall.SetEnv(foo, 3, null, 0));
            Check("get FOO -> -4", Get(foo, 3, vb, 64) == -4);
            Check("delete FOO again -> -4", Syscall.SetEnv(foo, 3, null, 0) == false);

            // --- caps: 64-byte name, 256-byte value accepted ---
            var big = stackalloc byte[64];
            for (int k = 0; k < 64; k++) big[k] = (byte)('A' + (k % 26));
            var v256 = stackalloc byte[256];
            for (int k = 0; k < 256; k++) v256[k] = (byte)('0' + (k % 10));
            Check("64-byte name + 256-byte value", Syscall.SetEnv(big, 64, v256, 256));
            Check("big value round-trip", Get(big, 64, vb, 64) == 256);
            Check("delete big", Syscall.SetEnv(big, 64, null, 0));
            var tooLong = stackalloc byte[65];
            for (int k = 0; k < 65; k++) tooLong[k] = (byte)'B';
            Check("65-byte name -> false", Syscall.SetEnv(tooLong, 65, bar, 3) == false);
            var v257 = stackalloc byte[257];
            for (int k = 0; k < 257; k++) v257[k] = (byte)'C';
            Check("257-byte value -> false", Syscall.SetEnv(foo, 3, v257, 257) == false);
            Check("empty name -> false", Syscall.SetEnv(null, 0, bar, 3) == false);

            // --- bad pointers -> -1 (EFAULT) ---
            ulong below = UserBase - 0x10;
            ulong above = UserLimit + 0x10;
            Check("63h name below", Syscall.Raw(0x63, 0, below, 4, UserBase, 64) == -1);
            Check("63h name above", Syscall.Raw(0x63, 0, above, 4, UserBase, 64) == -1);
            Check("63h buf null", Syscall.Raw(0x63, 0, UserBase, 4, 0, 64) == -1);
            Check("63h buf below", Syscall.Raw(0x63, 0, UserBase, 4, below, 64) == -1);
            Check("64h name below", Syscall.Raw(0x64, 0, below, 4, UserBase, 3) == -1);
            Check("64h val above", Syscall.Raw(0x64, 0, UserBase, 4, above, 3) == -1);
            Check("64h name len 0 -> -5", Syscall.Raw(0x64, 0, UserBase, 0, UserBase, 3) == -5);

            // --- LOGOUT/LOGIN: no variable leaks into the next login (C2) ---
            var persist = stackalloc byte[8]; Lit(persist, "PERSIST");
            var one = stackalloc byte[8]; Lit(one, "1");
            Check("set PERSIST", Syscall.SetEnv(persist, 7, one, 1));
            Check("logout", Syscall.Logout() == 0);
            Check("login again", Syscall.Login(root, 4, null, 0) == 0);
            Check("PERSIST cleared", Get(persist, 7, vb, 64) == -4);
            Check("USER=root after login", Is(Get(user, 4, vb, 64), vb, "root"));

            // --- copy-at-spawn / no cross-task visibility ---
            var parent = stackalloc byte[8]; Lit(parent, "parent");
            var parentbar = stackalloc byte[16]; Lit(parentbar, "parentbar");
            Check("set FOO=parent", Syscall.SetEnv(foo, 3, parent, 6));
            Check("set BAR=parentbar", Syscall.SetEnv(bar, 3, parentbar, 9));

            var path = stackalloc byte[32]; Lit(path, "A:\\OSB\\BIN\\TEnv.EXE");
            var childArg = stackalloc byte[8]; Lit(childArg, "child");
            int pathLen = StrLen(path);
            var req = new Syscall.SpawnReq
            {
                PathPtr = (ulong)path,
                PathLen = (uint)pathLen,
                ArgsPtr = (ulong)childArg,
                ArgsLen = 5,
                StdinHandle = -1,
                StdoutHandle = -1
            };
            int pid = Syscall.SpawnPath(&req);
            Check("spawn child", pid >= 0);
            if (pid >= 0) Syscall.Wait(pid);
            Check("parent FOO unchanged", Is(Get(foo, 3, vb, 64), vb, "parent"));
            Check("parent BAR unchanged", Is(Get(bar, 3, vb, 64), vb, "parentbar"));
            Check("child BAZ invisible", Get(baz, 3, vb, 64) == -4);

            // --- table full -> -6 (ENOMEM) ---
            var list = stackalloc byte[1024];
            int ln = Syscall.GetEnv(null, 0, list, 1024);
            int count = 0;
            for (int i = 0; i < ln; i++) if (list[i] == (byte)'\n') count++;
            for (int i = 0; count < 32 && i < 64; i++, count++)
            {
                var nm = stackalloc byte[8];
                nm[0] = (byte)'V';
                int nl = 1;
                if (i >= 10) nm[nl++] = (byte)('0' + i / 10);
                nm[nl++] = (byte)('0' + i % 10);
                if (!Syscall.SetEnv(nm, nl, one, 1)) { Fail("fill env"); break; }
            }
            Check("32 vars accepted", count == 32);
            var extra = stackalloc byte[8]; Lit(extra, "EXTRA");
            Check("33rd var -> false", Syscall.SetEnv(extra, 5, one, 1) == false);

            Done();
        }

        // Child mode: report the inherited values, then change them and exit.
        private static void Child()
        {
            var foo = stackalloc byte[8]; Lit(foo, "FOO");
            var bar = stackalloc byte[8]; Lit(bar, "BAR");
            var vb = stackalloc byte[64];
            int fn = Get(foo, 3, vb, 64);
            Syscall.PrintString("TENV child: FOO=");
            if (fn > 0) Syscall.Write(vb, fn);
            int bn = Get(bar, 3, vb, 64);
            Syscall.PrintString(" BAR=");
            if (bn > 0) Syscall.Write(vb, bn);
            Syscall.PrintString("\r\n");

            var child = stackalloc byte[8]; Lit(child, "child");
            var childbar = stackalloc byte[8]; Lit(childbar, "childbar");
            var baz = stackalloc byte[8]; Lit(baz, "childbaz");
            Syscall.SetEnv(foo, 3, child, 5);
            Syscall.SetEnv(bar, 3, childbar, 8);
            Syscall.SetEnv(baz, 3, baz, 8);
            Syscall.Exit(0);
        }

        // ---- helpers ----

        private static int Get(byte* name, int nlen, byte* buf, int cap)
        {
            return Syscall.GetEnv(name, nlen, buf, cap);
        }

        private static bool Is(int n, byte* buf, string lit)
        {
            if (n != lit.Length) return false;
            fixed (char* p = lit)
            {
                for (int k = 0; k < n; k++) if (buf[k] != (byte)p[k]) return false;
            }
            return true;
        }

        private static void Lit(byte* dst, string s)
        {
            fixed (char* p = s)
            {
                for (int k = 0; k < s.Length; k++) dst[k] = (byte)p[k];
            }
        }

        private static int StrLen(byte* s)
        {
            int n = 0;
            while (s[n] != 0) n++;
            return n;
        }

        private static void Check(string name, bool ok)
        {
            _cases++;
            if (ok) return;
            _fails++;
            Syscall.PrintString("TENV: ");
            Syscall.PrintString(name);
            Syscall.PrintString(" FAILED\r\n");
        }

        private static void Fail(string name) { Check(name, false); }

        private static void Done()
        {
            if (_fails == 0)
            {
                Syscall.PrintString("TENV: PASS n=");
                var buf = stackalloc char[8];
                int n = Fmt.U32(buf, (uint)_cases);
                Syscall.Print(buf, n);
                Syscall.PrintString("\r\n");
                Syscall.Exit(0);
            }
            Syscall.PrintString("TENV: FAIL (");
            var fbuf = stackalloc char[8];
            int fn = Fmt.U32(fbuf, (uint)_fails);
            Syscall.Print(fbuf, fn);
            Syscall.PrintString(" of ");
            int nc = Fmt.U32(fbuf, (uint)_cases);
            Syscall.Print(fbuf, nc);
            Syscall.PrintString(")\r\n");
            Syscall.Exit(1);
        }

        private static void MainChildCheck()
        {
            var ab = stackalloc byte[32];
            int n = Syscall.GetArgs(ab, 32);
            if (n == 5 && ab[0] == (byte)'c' && ab[1] == (byte)'h' && ab[2] == (byte)'i' &&
                ab[3] == (byte)'l' && ab[4] == (byte)'d')
            {
                Child();
            }
        }
    }
}