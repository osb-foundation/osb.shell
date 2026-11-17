using OsbKT;

namespace Osb.Tests
{
    // Silent exit 0. Used as the source image for ROOTONLY.EXE (a root-owned
    // 0700 file): spawning it when elevated runs silently; a non-root spawn is
    // refused with -3 before it runs.
    internal static unsafe class Program
    {
        private static void Main() { Syscall.Exit(0); }
    }
}