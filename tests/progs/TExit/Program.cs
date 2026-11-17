using OsbKT;

namespace Osb.Tests
{
    // Exits with code 7. The WAIT-proof lives in TWait (phase 1b): it
    // SPAWNPATHs this program, waits, and prints the code it got.
    internal static unsafe class Program
    {
        private static void Main()
        {
            Syscall.PrintString("TEXIT: exiting with code 7\r\n");
            Syscall.Exit(7);
        }
    }
}