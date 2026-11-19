using OsbKT;

namespace Osb.Tests
{
    // Tests STAT (60h), UNLINK (27h), RENAME (28h), COPY (29h), LINK (61h).
    internal static unsafe class Program
    {
        private static void Main()
        {
            var srcFile = "A:\\OSB\\TEMP\\SRC_FILE";
            var dstFile = "A:\\OSB\\TEMP\\DST_FILE";
            var linkFile = "A:\\OSB\\TEMP\\LINK_FILE";
            const int TestDataLen = 33;
            var testData = stackalloc byte[TestDataLen];
            testData[0] = (byte)'T'; testData[1] = (byte)'e'; testData[2] = (byte)'s'; testData[3] = (byte)'t'; testData[4] = (byte)' ';
            testData[5] = (byte)'d'; testData[6] = (byte)'a'; testData[7] = (byte)'t'; testData[8] = (byte)'a'; testData[9] = (byte)' ';
            testData[10] = (byte)'f'; testData[11] = (byte)'o'; testData[12] = (byte)'r'; testData[13] = (byte)' '; testData[14] = (byte)'f';
            testData[15] = (byte)'i'; testData[16] = (byte)'l'; testData[17] = (byte)'e'; testData[18] = (byte)' '; testData[19] = (byte)'m';
            testData[20] = (byte)'a'; testData[21] = (byte)'n'; testData[22] = (byte)'i'; testData[23] = (byte)'p'; testData[24] = (byte)'u';
            testData[25] = (byte)'l'; testData[26] = (byte)'a'; testData[27] = (byte)'t'; testData[28] = (byte)'i'; testData[29] = (byte)'o';
            testData[30] = (byte)'n'; testData[31] = (byte)'\r'; testData[32] = (byte)'\n';
            var outBuf = stackalloc byte[16];
            var readBuf = stackalloc byte[256];

            // Create source file
            int handle = Syscall.CreateFile(srcFile);
            if (handle < 0)
            {
                Syscall.PrintString("TFILEMANIP: CREATEFILE failed, code=");
                PrintInt(handle);
                Syscall.PrintString("\r\n");
                Syscall.Exit(1);
            }
            int written = Syscall.WriteFile(handle, testData, TestDataLen);
            if (written != TestDataLen)
            {
                Syscall.PrintString("TFILEMANIP: WRITEFILE failed\r\n");
                Syscall.Exit(1);
            }
            Syscall.Close(handle);

            // Test STAT
            if (!Syscall.Stat(srcFile, outBuf))
            {
                Syscall.PrintString("TFILEMANIP: STAT failed\r\n");
                Syscall.Exit(1);
            }
            // Parse STAT output: mode(2), uid(2), gid(2), size(4), mtime(4), links(2) = 16 bytes
            ulong size = 0;
            for (int k = 0; k < 4; k++) size |= ((ulong)outBuf[6 + k]) << (k * 8);
            if (size != (ulong)TestDataLen)
            {
                Syscall.PrintString("TFILEMANIP: STAT size mismatch: ");
                PrintInt((int)size);
                Syscall.PrintString(" expected ");
                PrintInt(TestDataLen);
                Syscall.PrintString("\r\n");
                Syscall.Exit(1);
            }
            Syscall.PrintString("TFILEMANIP: STAT ok, size=");
            PrintInt((int)size);
            Syscall.PrintString("\r\n");

            // Test COPY
            int result = Syscall.Copy(srcFile, dstFile);
            if (result != 0)
            {
                Syscall.PrintString("TFILEMANIP: COPY failed, code=");
                PrintInt(result);
                Syscall.PrintString("\r\n");
                Syscall.Exit(1);
            }
            Syscall.PrintString("TFILEMANIP: COPY ok\r\n");

            // Verify copy
            handle = Syscall.Open(dstFile);
            if (handle < 0)
            {
                Syscall.PrintString("TFILEMANIP: OPEN copy failed\r\n");
                Syscall.Exit(1);
            }
            int read = Syscall.Read(handle, readBuf, 256);
            if (read != TestDataLen)
            {
                Syscall.PrintString("TFILEMANIP: COPY verify read failed\r\n");
                Syscall.Exit(1);
            }
            for (int i = 0; i < TestDataLen; i++)
            {
                if (readBuf[i] != testData[i])
                {
                    Syscall.PrintString("TFILEMANIP: COPY data mismatch\r\n");
                    Syscall.Exit(1);
                }
            }
            Syscall.Close(handle);
            Syscall.PrintString("TFILEMANIP: COPY verify ok\r\n");

            // Test LINK (hard link)
            result = Syscall.Ln(srcFile, linkFile);
            if (result != 0)
            {
                Syscall.PrintString("TFILEMANIP: LINK failed, code=");
                PrintInt(result);
                Syscall.PrintString("\r\n");
                Syscall.Exit(1);
            }
            Syscall.PrintString("TFILEMANIP: LINK ok\r\n");

            // Verify link (stat should show link count = 2)
            if (!Syscall.Stat(srcFile, outBuf))
            {
                Syscall.PrintString("TFILEMANIP: STAT after LINK failed\r\n");
                Syscall.Exit(1);
            }
            ushort links = (ushort)(outBuf[14] | (outBuf[15] << 8));
            if (links != 2)
            {
                Syscall.PrintString("TFILEMANIP: LINK count mismatch: ");
                PrintInt(links);
                Syscall.PrintString(" expected 2\r\n");
                Syscall.Exit(1);
            }
            Syscall.PrintString("TFILEMANIP: LINK count ok (2)\r\n");

            // Test RENAME
            var renamedFile = "A:\\OSB\\TEMP\\RENAMED_FILE";
            result = Syscall.Rename(dstFile, renamedFile);
            if (result != 0)
            {
                Syscall.PrintString("TFILEMANIP: RENAME failed, code=");
                PrintInt(result);
                Syscall.PrintString("\r\n");
                Syscall.Exit(1);
            }
            Syscall.PrintString("TFILEMANIP: RENAME ok\r\n");

            // Verify renamed file exists and old doesn't
            if (Syscall.Stat(dstFile, outBuf))
            {
                Syscall.PrintString("TFILEMANIP: RENAME old path still exists\r\n");
                Syscall.Exit(1);
            }
            if (!Syscall.Stat(renamedFile, outBuf))
            {
                Syscall.PrintString("TFILEMANIP: RENAME new path not found\r\n");
                Syscall.Exit(1);
            }
            Syscall.PrintString("TFILEMANIP: RENAME verify ok\r\n");

            // Test UNLINK
            result = Syscall.Delete(renamedFile);
            if (result != 0)
            {
                Syscall.PrintString("TFILEMANIP: UNLINK failed, code=");
                PrintInt(result);
                Syscall.PrintString("\r\n");
                Syscall.Exit(1);
            }
            Syscall.PrintString("TFILEMANIP: UNLINK ok\r\n");

            // Verify unlinked
            if (Syscall.Stat(renamedFile, outBuf))
            {
                Syscall.PrintString("TFILEMANIP: UNLINK file still exists\r\n");
                Syscall.Exit(1);
            }
            Syscall.PrintString("TFILEMANIP: UNLINK verify ok\r\n");

            // Test LINK failure (EEXIST)
            result = Syscall.Ln(srcFile, linkFile);
            if (result != unchecked((int)0xFFFFFFF9)) // -7 EEXIST
            {
                Syscall.PrintString("TFILEMANIP: LINK EEXIST check failed, code=");
                PrintInt(result);
                Syscall.PrintString("\r\n");
                Syscall.Exit(1);
            }
            Syscall.PrintString("TFILEMANIP: LINK EEXIST ok\r\n");

            // Test STAT failure (ENOENT)
            if (Syscall.Stat("A:\\OSB\\TEMP\\NONEXISTENT", outBuf))
            {
                Syscall.PrintString("TFILEMANIP: STAT ENOENT check failed\r\n");
                Syscall.Exit(1);
            }
            Syscall.PrintString("TFILEMANIP: STAT ENOENT ok\r\n");

            // Cleanup
            Syscall.Delete(srcFile);
            Syscall.Delete(linkFile);

            Syscall.PrintString("TFILEMANIP: ALL TESTS PASS\r\n");
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