using OsbKT;

namespace Osb.Tests
{
    // O8.22 M1 — minimal Ring 3 native executable proof.
    //
    // Loaded by the real OSB loader (SPAWNPATH, AH=6Ah) from
    // A:\OSB\BIN\HELLO.EXE. Prints a recognizable message to the OSB console
    // via the real WRITE syscall (AH=01h) and terminates with exit code 42
    // via the real EXIT syscall (AH=4Ch, AL=42). No kernel internals, no
    // privileged instructions, no OSLANG involvement.
    internal static unsafe class Program
    {
        private static void Main()
        {
            Syscall.PrintString("OSB Ring 3 Native Hello World\r\n");
            Syscall.Exit(42);
        }
    }
}
