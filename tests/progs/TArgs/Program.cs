using OsbKT;

namespace Osb.Tests
{
    // Prints the task's argument string (GETARGS, 23h) verbatim.
    // Run as: TARGS.EXE one two three   ->   args=[one two three]
    internal static unsafe class Program
    {
        private static void Main()
        {
            var buf = stackalloc byte[128];
            int n = Syscall.GetArgs(buf, 128);
            Syscall.PrintString("args=[");
            if (n > 0) Syscall.Write(buf, n);
            Syscall.PrintString("]\r\n");
            Syscall.Exit(0);
        }
    }
}