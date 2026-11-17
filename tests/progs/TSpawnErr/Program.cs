using OsbKT;

namespace Osb.Tests
{
    // 26-spawnerr (spec, 1b): SPAWNPATH (6Ah) error contract (B3/B4/B5).
    //   * ENOENT (-4): a path that does not exist
    //   * EACCES (-3): a regular file with mode 0644 (TNX.EXE) -> refused, and
    //     NO child is created (TNX would print if it ran; it does not)
    //   * EINVAL (-5): argsLen 129, pathLen 0
    //   * EFAULT (-1): bad path pointer, bad args pointer
    //   * args delivered intact at 128 bytes: spawn TArgs.EXE with 128 bytes;
    //     TArgs prints all 128 (its GETARGS cap is 129 -> 128 chars).
    // Prints "TSPAWNERR: PASS n=<count>" only if every check holds.
    internal static unsafe class Program
    {
        private const ulong UserBase = 0x2000000;

        private static int _cases;
        private static int _fails;

        private static void Main()
        {
            // ENOENT: no such file.
            Check("ENOENT", Syscall.SpawnPath("A:\\OSB\\BIN\\NOPE.EXE", null), -4);

            // EACCES: TNX.EXE is a valid OSBEXE with mode 0644 (no x bit).
            // It must be refused and never run (no TNX output below).
            Check("EACCES", Syscall.SpawnPath("A:\\OSB\\BIN\\TNX.EXE", null), -3);

            // EINVAL: argsLen 129 (> 128). PathPtr in-window so only content fails.
            var argsBig = stackalloc byte[129];
            for (int i = 0; i < 129; i++) argsBig[i] = (byte)'A';
            Check("EINVAL-args129", SpawnRaw(argsBig, 129), -5);

            // EINVAL: pathLen 0.
            Check("EINVAL-pathlen0", SpawnRaw(null, 0, pathLen: 0), -5);

            // EFAULT: bad path pointer (below UserBase).
            Check("EFAULT-path", SpawnRaw(null, 0, pathPtr: UserBase - 0x10, pathLen: 8), -1);

            // EFAULT: bad args pointer (below UserBase), path in-window.
            Check("EFAULT-args", SpawnRaw(argsBig, 8, pathPtr: UserBase, pathLen: 8, argsPtr: UserBase - 0x10), -1);

            // args delivered intact at 128 bytes: spawn TArgs.EXE with 128 'A's.
            var pbuf = stackalloc byte[32];
            int plen = CopyPath("A:\\OSB\\BIN\\TArgs.EXE", pbuf, 32);
            var args128 = stackalloc byte[128];
            for (int i = 0; i < 128; i++) args128[i] = (byte)'A';
            var req = new Syscall.SpawnReq
            {
                PathPtr = (ulong)pbuf,
                PathLen = (uint)plen,
                ArgsPtr = (ulong)args128,
                ArgsLen = 128,
                StdinHandle = -1,
                StdoutHandle = -1
            };
            int child = Syscall.SpawnPath(&req);
            Check("spawn-args128", child >= 0 ? 0 : child, 0);
            if (child >= 0) Syscall.Wait(child);   // TArgs prints its args and exits

            if (_fails == 0)
            {
                Syscall.PrintString("TSPAWNERR: PASS n=");
                var nb = stackalloc char[8];
                int nn = Fmt.U32(nb, (uint)_cases);
                Syscall.Print(nb, nn);
                Syscall.PrintString("\r\n");
                Syscall.Exit(0);
            }
            else
            {
                Syscall.PrintString("TSPAWNERR: FAIL (");
                var b = stackalloc char[8];
                int n = Fmt.U32(b, (uint)_fails);
                Syscall.Print(b, n);
                Syscall.PrintString(" of ");
                int nc = Fmt.U32(b, (uint)_cases);
                Syscall.Print(b, nc);
                Syscall.PrintString(")\r\n");
                Syscall.Exit(1);
            }
        }

        // Build a SpawnReq on the stack and invoke 6Ah. PathPtr defaults to an
        // in-window dummy (UserBase) so content checks are isolated; argsPtr
        // defaults to 0 (no args).
        private static int SpawnRaw(byte* args, int argsLen, ulong pathPtr = 0, int pathLen = 8,
                                    ulong argsPtr = 0)
        {
            if (pathPtr == 0) pathPtr = UserBase;
            if (argsLen > 0 && argsPtr == 0) argsPtr = (ulong)args;
            var req = new Syscall.SpawnReq
            {
                PathPtr = pathPtr,
                PathLen = (uint)pathLen,
                ArgsPtr = argsPtr,
                ArgsLen = (uint)argsLen,
                StdinHandle = -1,
                StdoutHandle = -1
            };
            return Syscall.SpawnPath(&req);
        }

        private static int CopyPath(string s, byte* dst, int cap)
        {
            int len = s.Length;
            if (len > cap) len = cap;
            fixed (char* p = s)
            {
                for (int i = 0; i < len; i++) dst[i] = (byte)p[i];
            }
            return len;
        }

        private static void Check(string name, int got, int want)
        {
            _cases++;
            if (got != want)
            {
                _fails++;
                Syscall.PrintString("TSPAWNERR: ");
                Syscall.PrintString(name);
                Syscall.PrintString(" expected ");
                PrintInt(want);
                Syscall.PrintString(" got ");
                PrintInt(got);
                Syscall.PrintString("\r\n");
            }
        }

        private static void PrintInt(int v)
        {
            var buf = stackalloc char[16];
            int n = Fmt.S32(buf, v);
            Syscall.Print(buf, n);
        }
    }
}