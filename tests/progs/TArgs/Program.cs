using OsbKT;

namespace Osb.Tests
{
    // Prints the task's argument string (GETARGS, 23h) verbatim.
    // Run as: TARGS.EXE one two three   ->   args=[one two three]
    // The buffer/cap hold up to 128 bytes so a 128-byte argument round-trips
    // intact (cap 129 returns 128 chars + NUL).
    internal static unsafe class Program
    {
        private static void Main()
        {
            var buf = stackalloc byte[160];
            int n = Syscall.GetArgs(buf, 129);
            Syscall.PrintString("args=[");
            if (n > 0) Syscall.Write(buf, n);
            Syscall.PrintString("]\r\n");
            Syscall.Exit(0);
        }
    }
}