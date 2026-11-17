using OsbKT;

namespace Osb.Tests
{
    // F2 proof: text with '$' and a > 255-char line must survive.
    // The old AH=09h path stopped at the first '$' and truncated at 255;
    // WRITE (01h) with explicit length must print all of it.
    internal static unsafe class Program
    {
        private static void Main()
        {
            Syscall.PrintString("dollars: PROMPT=$USER@OSB $P$G\r\n");

            var big = stackalloc char[300];
            for (int i = 0; i < 300; i++) big[i] = 'A';
            Syscall.PrintString("len300: ");
            Syscall.Print(big, 300);
            Syscall.PrintString("\r\nend\r\n");
            Syscall.Exit(0);
        }
    }
}