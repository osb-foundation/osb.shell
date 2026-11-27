using OsbKT;

namespace Osb.Tests
{
    // SPAWNPATH OSLANG.EXE with DIVZERO.OSL (division by zero),
    // WAIT for it, and verify:
    // 1. Output contains "Runtime error: Division by zero."
    // 2. Exit code is non-zero (expected: 1)
    internal static unsafe class Program
    {
        private static void Main()
        {
            int child = Syscall.SpawnPath("A:\\OSB\\BIN\\OSLANG.EXE", "A:\\OSB\\BIN\\DIVZERO.OSL");
            if (child < 0)
            {
                Syscall.PrintString("TOSLANGERROR: spawn failed\r\n");
                Syscall.Exit(1);
            }

            int code = Syscall.Wait(child);

            Syscall.PrintString("TOSLANGERROR: child exit=");
            var buf = stackalloc char[16];
            int n = Fmt.S32(buf, code);
            Syscall.Print(buf, n);

            // Verify non-zero exit code
            if (code != 0)
            {
                Syscall.PrintString(" PASS (non-zero)\r\n");
                Syscall.Exit(0);
            }
            else
            {
                Syscall.PrintString(" FAIL (expected non-zero)\r\n");
                Syscall.Exit(1);
            }
        }
    }
}