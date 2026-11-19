using OsbKT;

namespace Osb.Tests
{
    // Tests OPEN (2Ah), READ (2Bh), WRITE (2Ch - legacy), CLOSE (2Ch),
    // CREATEFILE (76h), WRITEFILE (77h), SETSTDOUT (78h), SETSTDIN (79h).
    internal static unsafe class Program
    {
        private static void Main()
        {
            var testFile = "A:\\OSB\\TEMP\\TFILEIO_TEST";
            var stdoutFile = "A:\\OSB\\TEMP\\TFILEIO_STDOUT";
            const int TestDataLen = 22;
            var testData = stackalloc byte[TestDataLen];
            testData[0] = (byte)'H'; testData[1] = (byte)'e'; testData[2] = (byte)'l'; testData[3] = (byte)'l'; testData[4] = (byte)'o';
            testData[5] = (byte)','; testData[6] = (byte)' '; testData[7] = (byte)'O'; testData[8] = (byte)'S'; testData[9] = (byte)'B';
            testData[10] = (byte)' '; testData[11] = (byte)'F'; testData[12] = (byte)'i'; testData[13] = (byte)'l'; testData[14] = (byte)'e';
            testData[15] = (byte)' '; testData[16] = (byte)'I'; testData[17] = (byte)('/'); testData[18] = (byte)'O'; testData[19] = (byte)')';
            testData[20] = (byte)'\r'; testData[21] = (byte)'\n';
            var readBuf = stackalloc byte[256];

            // Test CREATEFILE
            int handle = Syscall.CreateFile(testFile);
            if (handle < 0)
            {
                Syscall.PrintString("TFILEIO: CREATEFILE failed, code=");
                PrintInt(handle);
                Syscall.PrintString("\r\n");
                Syscall.Exit(1);
            }
            Syscall.PrintString("TFILEIO: CREATEFILE handle=");
            PrintInt(handle);
            Syscall.PrintString("\r\n");

            // Test WRITEFILE
            int written = Syscall.WriteFile(handle, testData, TestDataLen);
            if (written != TestDataLen)
            {
                Syscall.PrintString("TFILEIO: WRITEFILE failed, written=");
                PrintInt(written);
                Syscall.PrintString(" expected=");
                PrintInt(TestDataLen);
                Syscall.PrintString("\r\n");
                Syscall.Exit(1);
            }
            Syscall.PrintString("TFILEIO: WRITEFILE ok, bytes=");
            PrintInt(written);
            Syscall.PrintString("\r\n");

            // Test SETSTDOUT (use separate file)
            int stdoutHandle = Syscall.CreateFile(stdoutFile);
            if (stdoutHandle >= 0)
            {
                Syscall.SetStdout(stdoutHandle);
                Syscall.PrintString("TFILEIO: SETSTDOUT to file\r\n");
                Syscall.SetStdout(-1);
                Syscall.PrintString("TFILEIO: SETSTDOUT back to console\r\n");
                Syscall.Close(stdoutHandle);
                Syscall.Delete(stdoutFile);
            }

            // Test READ
            Syscall.Close(handle);
            handle = Syscall.Open(testFile);
            if (handle < 0)
            {
                Syscall.PrintString("TFILEIO: OPEN failed, code=");
                PrintInt(handle);
                Syscall.PrintString("\r\n");
                Syscall.Exit(1);
            }
            int read = Syscall.Read(handle, readBuf, 256);
            if (read != TestDataLen)
            {
                Syscall.PrintString("TFILEIO: READ failed, read=");
                PrintInt(read);
                Syscall.PrintString(" expected=");
                PrintInt(TestDataLen);
                Syscall.PrintString("\r\n");
                Syscall.Exit(1);
            }
            Syscall.PrintString("TFILEIO: READ ok, bytes=");
            PrintInt(read);
            Syscall.PrintString("\r\n");

            // Test SETSTDIN
            Syscall.SetStdin(handle);
            Syscall.PrintString("TFILEIO: SETSTDIN from file\r\n");
            Syscall.SetStdin(-1);
            Syscall.PrintString("TFILEIO: SETSTDIN back to console\r\n");
            Syscall.Close(handle);

            // Cleanup
            Syscall.Delete(testFile);

            // Test OPEN failure (ENOENT)
            handle = Syscall.Open("A:\\OSB\\TEMP\\NONEXISTENT");
            if (handle != unchecked((int)0xFFFFFFFC)) // -4 ENOENT
            {
                Syscall.PrintString("TFILEIO: OPEN ENOENT check failed, code=");
                PrintInt(handle);
                Syscall.PrintString("\r\n");
                Syscall.Exit(1);
            }
            Syscall.PrintString("TFILEIO: OPEN ENOENT ok\r\n");

            Syscall.PrintString("TFILEIO: ALL TESTS PASS\r\n");
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