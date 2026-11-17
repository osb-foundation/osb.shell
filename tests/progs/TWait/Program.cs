using OsbKT;

namespace Osb.Tests
{
    // SPAWNPATH a program by path (6Ah, final SpawnReq form), WAIT for it,
    // and print "TWAIT: child exit=N PASS" only when the code is exactly 7.
    // The path/args are narrowed to bytes by the osbkt wrapper (B1); the
    // SpawnReq block is filled on the stack (zero GC allocations) and
    // stdin/stdout inherit the console.
    internal static unsafe class Program
    {
        private static void Main()
        {
            // "A:\OSB\BIN\TExit.EXE" (the staged name is TExit.EXE).
            int child = Syscall.SpawnPath("A:\\OSB\\BIN\\TExit.EXE", null);
            if (child < 0)
            {
                Syscall.PrintString("TWAIT: spawn failed\r\n");
                Syscall.Exit(1);
            }

            int code = Syscall.Wait(child);

            Syscall.PrintString("TWAIT: child exit=");
            var buf = stackalloc char[16];
            int n = Fmt.S32(buf, code);
            Syscall.Print(buf, n);
            if (code == 7) Syscall.PrintString(" PASS\r\n");
            else Syscall.PrintString(" FAIL\r\n");
            Syscall.Exit(code == 7 ? 0 : 1);
        }
    }
}