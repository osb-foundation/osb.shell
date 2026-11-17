using OsbKT;

namespace Osb.Tests
{
    // Spins forever (a background child for the 29-logout-kills test). Sleeps a
    // tick at a time so it yields to the scheduler; LOGOUT must kill it.
    internal static unsafe class Program
    {
        private static void Main()
        {
            for (;;) Syscall.Sleep(1);
        }
    }
}