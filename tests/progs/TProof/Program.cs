using OsbKT;

namespace Osb.Tests
{
    // M1 Ring 3 executable proof: prints a message via WRITE (01h) and
    // terminates via EXIT (4Ch) with code 0.
    internal static unsafe class Program
    {
        private static void Main()
        {
            Syscall.PrintString("M1: Ring 3 executable proof successful\r\n");
            Syscall.Exit(0);
        }
    }
}
