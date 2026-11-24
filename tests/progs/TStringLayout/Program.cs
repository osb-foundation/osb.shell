// TStringLayout - Minimal String Indexer Validation for M4.3.3
// Tests the MiniCoreLib String.this[int] implementation against verified NativeAOT layout.

using System;
using System.Runtime.InteropServices;
using OsbKT;

namespace Osb.Tests
{
    internal static unsafe class Program
    {
        // TLS helpers from osbnative (kernel)
        [DllImport("osbnative")]
        private static extern ulong osb_tls_tp();

        [DllImport("osbnative")]
        private static extern ulong osb_tls_get_alloc();

        [DllImport("osbnative")]
        private static extern ulong osb_tls_get_limit();

        [DllImport("osbnative")]
        private static extern void osb_tls_set_alloc(ulong v);

        [DllImport("osbnative")]
        private static extern void osb_tls_set_limit(ulong v);

        // Allocation helper from AllocFast_real.o
        [DllImport("osbnative")]
        private static extern ulong RhNewString(ulong mt, ulong len);

        // MethodTable buffer (same pattern as TAlloc)
        private struct MtBuf { public fixed byte b[32]; }
        private static MtBuf _mtString;

        private static int _fails = 0;
        private static int _checks = 0;

        public static void Main()
        {
            // Initialize TLS heap
            // Heap placed above the user stack ([0x207C000, 0x2080000)) and above
            // all TLS slots ([0x2100000, 0x2108000)), at UserTlsEnd.
            const ulong HeapBase = 0x2108000;
            const ulong HeapLimit = 0x2200000;
            osb_tls_set_alloc(HeapBase);
            osb_tls_set_limit(HeapLimit);

            // Initialize MethodTable buffer for RhNewString
            InitMt();

            Syscall.PrintString("TSTRINGLAYOUT: Starting...\r\n");

            // Test A: String literals
            TestLiterals();

            // Test B: Out-of-range behavior
            TestOutOfRange();

            // Test C: Heap-allocated strings (if feasible)
            TestHeapStrings();

            // Summary
            Syscall.PrintString("TSTRINGLAYOUT: Checks=");
            Syscall.PrintString(_checks.ToString());
            Syscall.PrintString(" Fails=");
            Syscall.PrintString(_fails.ToString());
            Syscall.PrintString("\r\n");

            if (_fails == 0)
            {
                Syscall.PrintString("TSTRINGLAYOUT: PASS\r\n");
                Syscall.Exit(0);
            }
            else
            {
                Syscall.PrintString("TSTRINGLAYOUT: FAIL\r\n");
                Syscall.Exit(1);
            }
        }

        private static void InitMt()
        {
            // Build a minimal MethodTable for System.String
            // The real AllocFast helpers only need the base size at offset 4
            // STRING_BASE_SIZE = 29 (from AllocFast.S)
            fixed (byte* p = _mtString.b)
            {
                // Offset 0: component size (WORD) = 2 for UTF-16
                *(ushort*)p = 2;
                // Offset 2: padding to align base size
                // Offset 4: base size (DWORD) = 29
                *(uint*)(p + 4) = 29;
            }
        }

        private static void Check(bool condition, string name)
        {
            _checks++;
            if (!condition)
            {
                _fails++;
                Syscall.PrintString("TSTRINGLAYOUT: FAIL ");
                Syscall.PrintString(name);
                Syscall.PrintString("\r\n");
            }
            else
            {
                Syscall.PrintString("TSTRINGLAYOUT: OK ");
                Syscall.PrintString(name);
                Syscall.PrintString("\r\n");
            }
        }

        private static void TestLiterals()
        {
            Syscall.PrintString("TSTRINGLAYOUT: Test A - String Literals\r\n");

            // Test "ABC"
            string s1 = "ABC";
            Check(s1.Length == 3, "ABC.Length");
            Check(s1[0] == 'A', "ABC[0]=='A'");
            Check(s1[1] == 'B', "ABC[1]=='B'");
            Check(s1[2] == 'C', "ABC[2]=='C'");

            // Test "OSLANG"
            string s2 = "OSLANG";
            Check(s2.Length == 6, "OSLANG.Length");
            Check(s2[0] == 'O', "OSLANG[0]=='O'");
            Check(s2[2] == 'L', "OSLANG[2]=='L'");
            Check(s2[5] == 'G', "OSLANG[5]=='G'");

            // Test empty string
            string empty = "";
            Check(empty.Length == 0, "empty.Length");
            Check(empty[0] == '\0', "empty[0]=='\\0' (OOB)");

            // Test mixed case
            string mixed = "HeLlO";
            Check(mixed.Length == 5, "HeLlO.Length");
            Check(mixed[0] == 'H', "HeLlO[0]=='H'");
            Check(mixed[1] == 'e', "HeLlO[1]=='e'");
            Check(mixed[2] == 'L', "HeLlO[2]=='L'");
            Check(mixed[3] == 'l', "HeLlO[3]=='l'");
            Check(mixed[4] == 'O', "HeLlO[4]=='O'");

            // Test string with space
            string withSpace = "A B";
            Check(withSpace.Length == 3, "A B.Length");
            Check(withSpace[0] == 'A', "A B[0]=='A'");
            Check(withSpace[1] == ' ', "A B[1]==' '");
            Check(withSpace[2] == 'B', "A B[2]=='B'");

            // Test longer string
            string longer = "HELLO WORLD";
            Check(longer.Length == 11, "HELLO WORLD.Length");
            Check(longer[0] == 'H', "HELLO[0]");
            Check(longer[5] == ' ', "HELLO[5]==' '");
            Check(longer[10] == 'D', "HELLO[10]=='D'");
        }

        private static void TestOutOfRange()
        {
            Syscall.PrintString("TSTRINGLAYOUT: Test B - Out of Range\r\n");

            string s = "TEST";

            // Negative index
            Check(s[-1] == '\0', "s[-1]=='\\0'");
            Check(s[-100] == '\0', "s[-100]=='\\0'");

            // Index equal to Length
            Check(s[4] == '\0', "s[Length]=='\\0'");

            // Index greater than Length
            Check(s[5] == '\0', "s[Length+1]=='\\0'");
            Check(s[100] == '\0', "s[100]=='\\0'");
        }

        private static void TestHeapStrings()
        {
            Syscall.PrintString("TSTRINGLAYOUT: Test C - Heap Strings\r\n");

            // Try to allocate a string via RhNewString
            ulong mt;
            fixed (byte* p = _mtString.b) mt = (ulong)p;

            // Allocate a 4-character string (needs 29 + 2*4 = 37 -> aligned to 40 bytes)
            ulong ptr = RhNewString(mt, 4);

            if (ptr == 0)
            {
                Syscall.PrintString("TSTRINGLAYOUT: RhNewString returned 0 (OOM or fallback)\r\n");
                Check(false, "RhNewString allocation");
                return;
            }

            // Verify the allocated object has correct MethodTable and Length
            Check(*(ulong*)ptr == mt, "HeapString.MethodTable");
            Check(*(uint*)(ptr + 8) == 4, "HeapString.Length");

            // Write "WXYZ" to the character data at offset 12
            char* chars = (char*)(ptr + 12);
            chars[0] = 'W';
            chars[1] = 'X';
            chars[2] = 'Y';
            chars[3] = 'Z';

            // Now we need to test the managed indexer on this heap string.
            // In a proper runtime, we'd use RhBox or similar to get a managed reference.
            // Without that, we can't safely cast the raw pointer to a string.
            // Document this limitation.
            Syscall.PrintString("TSTRINGLAYOUT: Heap string allocated but managed cast not available\r\n");
            Check(true, "HeapString.Allocation"); // Allocation itself succeeded
        }
    }
}