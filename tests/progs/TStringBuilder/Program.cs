// TStringBuilder - StringBuilder.ToString() Validation for M4.3.4
// Tests the MiniCoreLib StringBuilder.ToString() implementation.

using System;
using System.Text;
using System.Runtime.InteropServices;
using OsbKT;

namespace Osb.Tests
{
    internal static unsafe class Program
    {
        [DllImport("osbnative")]
        private static extern ulong osb_tls_get_alloc();

        [DllImport("osbnative")]
        private static extern ulong osb_tls_get_limit();

        [DllImport("osbnative")]
        private static extern void osb_tls_set_alloc(ulong v);

        [DllImport("osbnative")]
        private static extern void osb_tls_set_limit(ulong v);

        private static int _fails = 0;
        private static int _checks = 0;

        public static void Main()
        {
            // Initialize TLS heap
            const ulong HeapBase = 0x207C000;
            const ulong HeapLimit = 0x2200000;
            osb_tls_set_alloc(HeapBase);
            osb_tls_set_limit(HeapLimit);

            Syscall.PrintString("TSTRINGBUILDER: Starting...\r\n");

            // Test 1: Empty StringBuilder
            TestEmpty();

            // Test 2: Single character
            TestSingleChar();

            // Test 3: Multiple characters
            TestMultipleChars();

            // Test 4: Mixed case
            TestMixedCase();

            // Test 5: With spaces
            TestWithSpaces();

            // Test 6: Append string
            TestAppendString();

            // Test 7: Append char array
            TestAppendCharArray();

            // Test 8: AppendLine
            TestAppendLine();

            // Test 9: Clear
            TestClear();

            // Test 10: Capacity growth
            TestCapacityGrowth();

            // Test 11: Independence from subsequent modifications
            TestIndependence();

            // Summary
            Syscall.PrintString("TSTRINGBUILDER: Checks=");
            Syscall.PrintString(_checks.ToString());
            Syscall.PrintString(" Fails=");
            Syscall.PrintString(_fails.ToString());
            Syscall.PrintString("\r\n");

            if (_fails == 0)
            {
                Syscall.PrintString("TSTRINGBUILDER: PASS\r\n");
                Syscall.Exit(0);
            }
            else
            {
                Syscall.PrintString("TSTRINGBUILDER: FAIL\r\n");
                Syscall.Exit(1);
            }
        }

        private static void Check(bool condition, string name)
        {
            _checks++;
            if (!condition)
            {
                _fails++;
                Syscall.PrintString("TSTRINGBUILDER: FAIL ");
                Syscall.PrintString(name);
                Syscall.PrintString("\r\n");
            }
            else
            {
                Syscall.PrintString("TSTRINGBUILDER: OK ");
                Syscall.PrintString(name);
                Syscall.PrintString("\r\n");
            }
        }

        private static void TestEmpty()
        {
            Syscall.PrintString("TSTRINGBUILDER: Test 1 - Empty StringBuilder\r\n");
            var sb = new StringBuilder();
            string result = sb.ToString();
            Check(result.Length == 0, "Empty.Length==0");
            Check(result[0] == '\0', "Empty[0]=='\\0'");
        }

        private static void TestSingleChar()
        {
            Syscall.PrintString("TSTRINGBUILDER: Test 2 - Single Char\r\n");
            var sb = new StringBuilder();
            sb.Append('X');
            string result = sb.ToString();
            Check(result.Length == 1, "SingleChar.Length==1");
            Check(result[0] == 'X', "SingleChar[0]=='X'");
        }

        private static void TestMultipleChars()
        {
            Syscall.PrintString("TSTRINGBUILDER: Test 3 - Multiple Chars\r\n");
            var sb = new StringBuilder();
            sb.Append('A').Append('B').Append('C');
            string result = sb.ToString();
            Check(result.Length == 3, "MultiChar.Length==3");
            Check(result[0] == 'A', "MultiChar[0]=='A'");
            Check(result[1] == 'B', "MultiChar[1]=='B'");
            Check(result[2] == 'C', "MultiChar[2]=='C'");
        }

        private static void TestMixedCase()
        {
            Syscall.PrintString("TSTRINGBUILDER: Test 4 - Mixed Case\r\n");
            var sb = new StringBuilder();
            sb.Append('H').Append('e').Append('L').Append('l').Append('O');
            string result = sb.ToString();
            Check(result.Length == 5, "MixedCase.Length==5");
            Check(result[0] == 'H', "MixedCase[0]=='H'");
            Check(result[1] == 'e', "MixedCase[1]=='e'");
            Check(result[2] == 'L', "MixedCase[2]=='L'");
            Check(result[3] == 'l', "MixedCase[3]=='l'");
            Check(result[4] == 'O', "MixedCase[4]=='O'");
        }

        private static void TestWithSpaces()
        {
            Syscall.PrintString("TSTRINGBUILDER: Test 5 - With Spaces\r\n");
            var sb = new StringBuilder();
            sb.Append('H').Append(' ').Append('W');
            string result = sb.ToString();
            Check(result.Length == 3, "WithSpaces.Length==3");
            Check(result[0] == 'H', "WithSpaces[0]=='H'");
            Check(result[1] == ' ', "WithSpaces[1]==' '");
            Check(result[2] == 'W', "WithSpaces[2]=='W'");
        }

        private static void TestAppendString()
        {
            Syscall.PrintString("TSTRINGBUILDER: Test 6 - Append String\r\n");
            var sb = new StringBuilder();
            sb.Append("Hello");
            string result = sb.ToString();
            Check(result.Length == 5, "AppendString.Length==5");
            Check(result[0] == 'H', "AppendString[0]=='H'");
            Check(result[4] == 'o', "AppendString[4]=='o'");
        }

        private static void TestAppendCharArray()
        {
            Syscall.PrintString("TSTRINGBUILDER: Test 7 - Append Char Array\r\n");
            var sb = new StringBuilder();
            char[] arr = new char[] { 'A', 'B', 'C', 'D' };
            sb.Append(arr);
            string result = sb.ToString();
            Check(result.Length == 4, "AppendCharArray.Length==4");
            Check(result[0] == 'A', "AppendCharArray[0]=='A'");
            Check(result[1] == 'B', "AppendCharArray[1]=='B'");
            Check(result[2] == 'C', "AppendCharArray[2]=='C'");
            Check(result[3] == 'D', "AppendCharArray[3]=='D'");
        }

        private static void TestAppendLine()
        {
            Syscall.PrintString("TSTRINGBUILDER: Test 8 - AppendLine\r\n");
            var sb = new StringBuilder();
            sb.AppendLine("Line1");
            sb.AppendLine("Line2");
            string result = sb.ToString();
            Check(result.Length == 12, "AppendLine.Length==12");
            Check(result[5] == '\n', "AppendLine[5]=='\\n'");
            Check(result[11] == '\n', "AppendLine[11]=='\\n'");
        }

        private static void TestClear()
        {
            Syscall.PrintString("TSTRINGBUILDER: Test 9 - Clear\r\n");
            var sb = new StringBuilder();
            sb.Append("Test");
            sb.Clear();
            string result = sb.ToString();
            Check(result.Length == 0, "Clear.Length==0");
            Check(result[0] == '\0', "Clear[0]=='\\0'");
        }

        private static void TestCapacityGrowth()
        {
            Syscall.PrintString("TSTRINGBUILDER: Test 10 - Capacity Growth\r\n");
            var sb = new StringBuilder(4);
            sb.Append("0123456789");
            string result = sb.ToString();
            Check(result.Length == 10, "CapacityGrowth.Length==10");
            Check(result[0] == '0', "CapacityGrowth[0]=='0'");
            Check(result[9] == '9', "CapacityGrowth[9]=='9'");
        }

        private static void TestIndependence()
        {
            Syscall.PrintString("TSTRINGBUILDER: Test 11 - Independence\r\n");
            var sb = new StringBuilder();
            sb.Append("Original");
            string result1 = sb.ToString();
            // Check result1 BEFORE creating result2
            bool r1Len8b = (result1.Length == 8);
            bool r1C0Ob = (result1[0] == 'O');
            bool r1C7Lb = (result1[7] == 'l');
            sb.Append("Modified");
            string result2 = sb.ToString();
            char c1_0 = result1[0];
            char c1_7 = result1[7];
            char c2_8 = result2[8];
            bool r1Len8 = (result1.Length == 8);
            bool r1C0O = (c1_0 == 'O');
            bool r1C7L = (c1_7 == 'l');
            bool r2Len16 = (result2.Length == 16);
            bool r2C8M = (c2_8 == 'M');
            Check(r1Len8b, "Independence.Result1.Length==8 (before)");
            Check(r1C0Ob, "Independence.Result1[0]=='O' (before)");
            Check(r1C7Lb, "Independence.Result1[7]=='l' (before)");
            Check(r1Len8, "Independence.Result1.Length==8 (after)");
            Check(r1C0O, "Independence.Result1[0]=='O' (after)");
            Check(r1C7L, "Independence.Result1[7]=='l' (after)");
            Check(r2Len16, "Independence.Result2.Length==16");
            Check(r2C8M, "Independence.Result2[8]=='M'");
        }
    }
}
