using OsbKT;

namespace Osb.Shell.Userspace
{
    // Phase 1a milestone shell: verifies the kernel ABI, prints a banner, and
    // idles. Interactive input (GETCHAR) lands in phase 1b and commands follow
    // slice by slice - see docs/MIGRATION.md. Zero GC allocations by design.
    internal static unsafe class Program
    {
        private const string Banner =
            "OSB Userspace Shell v0.72 (phase 1a)\r\n" +
            "Input support arrives in phase 1b; see docs/MIGRATION.md.\r\n";

        private static void Main()
        {
            ushort abi = Syscall.GetABI();
            if (abi != Syscall.AbiVersion)
            {
                Syscall.PrintString("SHELL: kernel ABI mismatch, halting.\r\n");
                return;
            }

            Syscall.PrintString(Banner);

            while (true)
            {
                Syscall.Sleep(100); // idle in ~100-tick slices; IRQ-driven
            }
        }
    }
}