using OsbKT;

namespace Osb.Tests
{
    // 6Ah proof: SPAWNPATH a file on disk (not a built-in), WAIT for it, and
    // print the exit code it reported. TEXIT.EXE exits with 7, so we PASS only
    // when the kernel hands back exactly 7. The path length is derived from the
    // literal (review item 4), never hardcoded.
    internal static unsafe class Program
    {
        private static void Main()
        {
            string path = "A:\\OSB\\BIN\\TExit.EXE";
            fixed (char* p = path)
            {
                int child = Syscall.SpawnPath(p, path.Length, null, 0);
                if (child < 0)
                {
                    Syscall.PrintString("TWAIT: spawn failed\r\n");
                    Syscall.Exit(1);
                }

                int code = Syscall.Wait(child);

                Syscall.PrintString("TWAIT: child exit=");
                var buf = stackalloc char[16];
                int n = Fmt.S32(buf, code);
                Syscall.Print(buf, n);
                if (code == 7) Syscall.PrintString(" PASS\r\n");
                else Syscall.PrintString(" FAIL\r\n");
                Syscall.Exit(code == 7 ? 0 : 1);
            }
        }
    }
}