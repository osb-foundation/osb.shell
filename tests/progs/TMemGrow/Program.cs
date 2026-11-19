using OsbKT;

namespace Osb.Tests
{
    // Tests MEMGROW (11h) - memory allocation in 2 MiB steps.
    internal static unsafe class Program
    {
        private static void Main()
        {
            // Query current limit (RDI = 0)
            int current = Syscall.MemGrow(0);
            if (current < 0)
            {
                Syscall.PrintString("TMEMCGROW: Query failed, code=");
                PrintInt(current);
                Syscall.PrintString("\r\n");
                Syscall.Exit(1);
            }
            Syscall.PrintString("TMEMCGROW: Current frames=");
            PrintInt(current);
            Syscall.PrintString("\r\n");

            // Request 512 frames (2 MiB)
            int result = Syscall.MemGrow(512);
            if (result != 0)
            {
                Syscall.PrintString("TMEMCGROW: Alloc 512 failed, code=");
                PrintInt(result);
                Syscall.PrintString("\r\n");
                Syscall.Exit(1);
            }
            Syscall.PrintString("TMEMCGROW: Alloc 512 ok\r\n");

            // Verify new limit
            int newLimit = Syscall.MemGrow(0);
            if (newLimit != current + 512)
            {
                Syscall.PrintString("TMEMCGROW: Limit mismatch: ");
                PrintInt(newLimit);
                Syscall.PrintString(" expected ");
                PrintInt(current + 512);
                Syscall.PrintString("\r\n");
                Syscall.Exit(1);
            }
            Syscall.PrintString("TMEMCGROW: Limit verify ok\r\n");

            // Request another 512 frames
            result = Syscall.MemGrow(512);
            if (result != 0)
            {
                Syscall.PrintString("TMEMCGROW: Alloc 512 again failed, code=");
                PrintInt(result);
                Syscall.PrintString("\r\n");
                Syscall.Exit(1);
            }
            Syscall.PrintString("TMEMCGROW: Alloc 512 again ok\r\n");

            // Test EINVAL: not multiple of 512
            result = Syscall.MemGrow(100);
            if (result != unchecked((int)0xFFFFFFFB)) // -5 EINVAL
            {
                Syscall.PrintString("TMEMCGROW: EINVAL check failed, code=");
                PrintInt(result);
                Syscall.PrintString(" expected -5\r\n");
                Syscall.Exit(1);
            }
            Syscall.PrintString("TMEMCGROW: EINVAL (not multiple of 512) ok\r\n");

            // Test EINVAL: exceeds per-process limit (4096 frames = 16 MiB)
            // current + 512 + 512 = current + 1024. Try to add enough to exceed 4096.
            int remaining = 4096 - newLimit - 512;
            if (remaining > 0)
            {
                result = Syscall.MemGrow(remaining + 512); // exceed limit
                if (result != unchecked((int)0xFFFFFFFB)) // -5 EINVAL
                {
                    Syscall.PrintString("TMEMCGROW: EINVAL limit check failed, code=");
                    PrintInt(result);
                    Syscall.PrintString(" expected -5\r\n");
                    Syscall.Exit(1);
                }
                Syscall.PrintString("TMEMCGROW: EINVAL (exceeds limit) ok\r\n");
            }

            Syscall.PrintString("TMEMCGROW: ALL TESTS PASS\r\n");
            Syscall.Exit(0);
        }

        private static void PrintInt(int value)
        {
            var buf = stackalloc char[16];
            int n = Fmt.S32(buf, value);
            Syscall.Print(buf, n);
        }
    }
}