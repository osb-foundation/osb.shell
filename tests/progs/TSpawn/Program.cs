using OsbKT;

namespace Osb.Tests
{
    // SPAWN(0) = HELLO built-in, WAIT for it, print the exit code.
    // Proves Spawn(id) picks the right built-in (HELLO's banner appears on
    // the console between our lines) and that WAIT returns its exit code.
    internal static unsafe class Program
    {
        private static void Main()
        {
            byte pid = 0; // ProgHello
            int child = Syscall.Spawn(pid);
            if (child < 0)
            {
                Syscall.PrintString("TSPAWN: spawn failed\r\n");
                Syscall.Exit(1);
            }

            int code = Syscall.Wait(child);

            Syscall.PrintString("TSPAWN: child exit=");
            var buf = stackalloc char[16];
            int n = Fmt.S32(buf, code);
            Syscall.Print(buf, n);
            if (code == 0) Syscall.PrintString(" PASS\r\n");
            else Syscall.PrintString(" FAIL\r\n");
            Syscall.Exit(code == 0 ? 0 : 1);
        }
    }
}