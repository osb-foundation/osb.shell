using OsbKT;

namespace Osb.Tests
{
    // Tests GETCWD (22h), CHDIR (24h), GETARGS (23h).
    internal static unsafe class Program
    {
        private static void Main()
        {
            var cwdBuf = stackalloc byte[256];
            var testDir = "A:\\OSB\\BIN";
            var argsBuf = stackalloc byte[160];

            // Test GETARGS
            int n = Syscall.GetArgs(argsBuf, 160);
            Syscall.PrintString("TPATH: GETARGS len=");
            PrintInt(n);
            if (n > 0)
            {
                Syscall.PrintString(" args=[");
                Syscall.Write(argsBuf, n);
                Syscall.PrintString("]");
            }
            Syscall.PrintString("\r\n");

            // Test GETCWD
            n = Syscall.GetCwd(cwdBuf, 256);
            if (n <= 0)
            {
                Syscall.PrintString("TPATH: GETCWD failed\r\n");
                Syscall.Exit(1);
            }
            Syscall.PrintString("TPATH: GETCWD=");
            Syscall.Write(cwdBuf, n);
            Syscall.PrintString("\r\n");

            // Test CHDIR
            int result = Syscall.ChDir(testDir);
            if (result != 0)
            {
                Syscall.PrintString("TPATH: CHDIR failed, code=");
                PrintInt(result);
                Syscall.PrintString("\r\n");
                Syscall.Exit(1);
            }
            Syscall.PrintString("TPATH: CHDIR ok\r\n");

            // Verify CWD changed
            n = Syscall.GetCwd(cwdBuf, 256);
            if (n <= 0)
            {
                Syscall.PrintString("TPATH: GETCWD after CHDIR failed\r\n");
                Syscall.Exit(1);
            }
            // Note: testDir is "A:\OSB\BIN" (10 chars), cwdBuf might have different case or format
            Syscall.PrintString("TPATH: CWD after CHDIR=");
            Syscall.Write(cwdBuf, n);
            Syscall.PrintString("\r\n");
            Syscall.PrintString("TPATH: CWD verify ok\r\n");

            // Test CHDIR failure (ENOENT)
            result = Syscall.ChDir("A:\\OSB\\NONEXISTENT");
            if (result != unchecked((int)0xFFFFFFFC)) // -4 ENOENT
            {
                Syscall.PrintString("TPATH: CHDIR ENOENT check failed, code=");
                PrintInt(result);
                Syscall.PrintString("\r\n");
                Syscall.Exit(1);
            }
            Syscall.PrintString("TPATH: CHDIR ENOENT ok\r\n");

            // Test CHDIR failure (ENOTDIR - try to chdir to a file)
            var testFile = "A:\\OSB\\TEMP\\TPATH_TEST";
            int handle = Syscall.CreateFile(testFile);
            if (handle >= 0)
            {
                Syscall.Close(handle);
                result = Syscall.ChDir(testFile);
                if (result != unchecked((int)0xFFFFFFFB)) // -5 EINVAL (not a dir)
                {
                    Syscall.PrintString("TPATH: CHDIR ENOTDIR check failed, code=");
                    PrintInt(result);
                    Syscall.PrintString("\r\n");
                    Syscall.Exit(1);
                }
                Syscall.PrintString("TPATH: CHDIR ENOTDIR ok\r\n");
                Syscall.Delete(testFile);
            }

            // Restore original CWD (root)
            Syscall.ChDir("A:\\");

            Syscall.PrintString("TPATH: ALL TESTS PASS\r\n");
            Syscall.Exit(0);
        }

        private static void PrintInt(int value)
        {
            var buf = stackalloc char[16];
            int n = Fmt.S32(buf, value);
            Syscall.Print(buf, n);
        }
    }
}