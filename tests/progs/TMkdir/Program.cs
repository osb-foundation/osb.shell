using OsbKT;

namespace Osb.Tests
{
    // Tests MKDIR (25h), RMDIR (26h), and DIRNEXT (21h via inode).
    internal static unsafe class Program
    {
        private static void Main()
        {
            // Test MKDIR
            var testDir = "A:\\OSB\\TEMP\\MKDIR_TEST";
            int result = Syscall.Mkdir(testDir);
            if (result != 0)
            {
                Syscall.PrintString("TMKDIR: MKDIR failed, code=");
                PrintInt(result);
                Syscall.PrintString("\r\n");
                Syscall.Exit(1);
            }
            Syscall.PrintString("TMKDIR: MKDIR ok\r\n");

            // Test RESOLVE + DIRNEXT
            var parentDir = "A:\\OSB\\TEMP";
            if (Syscall.ResolvePath(parentDir, out uint dirIno, out _, out _, out _))
            {
                int entryCount = 0;
                var outBuf = stackalloc byte[256];
                for (int i = 0; i < 100; i++)
                {
                    if (!Syscall.DirNext(dirIno, i, outBuf, 256))
                        break;
                    entryCount++;
                }
                Syscall.PrintString("TMKDIR: DIRNEXT entries=");
                PrintInt(entryCount);
                Syscall.PrintString("\r\n");
            }
            else
            {
                Syscall.PrintString("TMKDIR: RESOLVE failed\r\n");
                Syscall.Exit(1);
            }

            // Test RMDIR
            var testDir2 = "A:\\OSB\\TEMP\\MKDIR_TEST";
            result = Syscall.Rmdir(testDir2);
            if (result != 0)
            {
                Syscall.PrintString("TMKDIR: RMDIR failed, code=");
                PrintInt(result);
                Syscall.PrintString("\r\n");
                Syscall.Exit(1);
            }
            Syscall.PrintString("TMKDIR: RMDIR ok\r\n");

            // Test MKDIR failure (EEXIST)
            var testDir3 = "A:\\OSB\\TEMP\\MKDIR_TEST";
            result = Syscall.Mkdir(testDir3);
            if (result != 0)
            {
                Syscall.PrintString("TMKDIR: MKDIR again failed, code=");
                PrintInt(result);
                Syscall.PrintString("\r\n");
                Syscall.Exit(1);
            }
            result = Syscall.Mkdir(testDir3);
            if (result != unchecked((int)0xFFFFFFF9)) // -7 EEXIST
            {
                Syscall.PrintString("TMKDIR: MKDIR EEXIST check failed, code=");
                PrintInt(result);
                Syscall.PrintString("\r\n");
                Syscall.Exit(1);
            }
            Syscall.PrintString("TMKDIR: MKDIR EEXIST ok\r\n");

            // Cleanup
            Syscall.Rmdir(testDir3);

            // Test RMDIR failure (ENOTEMPTY) - create dir with a file inside
            var testDir4 = "A:\\OSB\\TEMP\\MKDIR_TEST2";
            result = Syscall.Mkdir(testDir4);
            if (result != 0)
            {
                Syscall.PrintString("TMKDIR: MKDIR2 failed, code=");
                PrintInt(result);
                Syscall.PrintString("\r\n");
                Syscall.Exit(1);
            }

            // Create a file inside
            var testFile = "A:\\OSB\\TEMP\\MKDIR_TEST2\\test.txt";
            int handle = Syscall.CreateFile(testFile);
            if (handle < 0)
            {
                Syscall.PrintString("TMKDIR: CREATEFILE failed, code=");
                PrintInt(handle);
                Syscall.PrintString("\r\n");
                Syscall.Exit(1);
            }
            Syscall.Close(handle);

            // Try to RMDIR non-empty dir
            result = Syscall.Rmdir(testDir4);
            if (result != unchecked((int)0xFFFFFFFB)) // -5 ENOTEMPTY
            {
                Syscall.PrintString("TMKDIR: RMDIR ENOTEMPTY check failed, code=");
                PrintInt(result);
                Syscall.PrintString("\r\n");
                Syscall.Exit(1);
            }
            Syscall.PrintString("TMKDIR: RMDIR ENOTEMPTY ok\r\n");

            // Cleanup
            Syscall.Delete(testFile);
            Syscall.Rmdir(testDir4);

            Syscall.PrintString("TMKDIR: ALL TESTS PASS\r\n");
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