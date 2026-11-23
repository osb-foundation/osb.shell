using System;
using System.Runtime.InteropServices;
using OsbKT;

namespace Osb.Tests
{
    internal static unsafe class Program
    {
        [DllImport("osbnative")] static extern ulong osb_tls_tp();
        [DllImport("osbnative")] static extern ulong osb_tls_get_alloc();
        [DllImport("osbnative")] static extern ulong osb_tls_get_limit();
        [DllImport("osbnative")] static extern void osb_tls_set_alloc(ulong v);
        [DllImport("osbnative")] static extern void osb_tls_set_limit(ulong v);

        private static void Main()
        {
            // Initialize TLS heap
            const ulong HeapBase = 0x207C000;
            const ulong HeapLimit = 0x2200000;
            osb_tls_set_alloc(HeapBase);
            osb_tls_set_limit(HeapLimit);

            Syscall.PrintString("TEETYPELAYOUT: Starting...\r\n");

            // Test char[] EEType
            var charArr = new char[0];
            Syscall.PrintString("TEETYPELAYOUT: Testing char[]...\r\n");
            PrintEETypeInfo("char[]", charArr);

            // Test byte[]
            var byteArr = new byte[0];
            Syscall.PrintString("TEETYPELAYOUT: Testing byte[]...\r\n");
            PrintEETypeInfo("byte[]", byteArr);

            // Test int[]
            var intArr = new int[0];
            Syscall.PrintString("TEETYPELAYOUT: Testing int[]...\r\n");
            PrintEETypeInfo("int[]", intArr);

            // Test string[] (reference array)
            var strArr = new string[0];
            Syscall.PrintString("TEETYPELAYOUT: Testing string[]...\r\n");
            PrintEETypeInfo("string[]", strArr);

            Syscall.PrintString("TEETYPELAYOUT: Done.\r\n");
        }

        private static void PrintEETypeInfo(string name, Array arr)
        {
            // arr is a managed reference (pointer) to the array object
            // Get the array object pointer by dereferencing the local variable address
            ulong arrObjPtr = *(ulong*)(&arr);
            
            Syscall.PrintString("TEETYPELAYOUT: ");
            Syscall.PrintString(name);
            Syscall.PrintString(" arrObjPtr=0x");
            PrintHex(arrObjPtr);
            Syscall.PrintString("\r\n");
            
            if (arrObjPtr != 0)
            {
                // The array object has the EEType pointer at offset 0
                ulong eeTypePtr = *(ulong*)arrObjPtr;
                Syscall.PrintString("TEETYPELAYOUT:   eeType=0x");
                PrintHex(eeTypePtr);
                Syscall.PrintString("\r\n");
                
                if (eeTypePtr != 0)
                {
                    // Read first few qwords of EEType
                    for (int i = 0; i < 4; i++)
                    {
                        ulong val = *(ulong*)(eeTypePtr + (ulong)i * 8);
                        Syscall.PrintString("TEETYPELAYOUT:   [");
                        PrintHex((ulong)i * 8);
                        Syscall.PrintString("] = 0x");
                        PrintHex(val);
                        Syscall.PrintString("\r\n");
                    }
                    
                    // Read bytes around offset 10 for component size
                    ushort compSize = *(ushort*)(eeTypePtr + 10);
                    Syscall.PrintString("TEETYPELAYOUT:   Component size at offset 10 = ");
                    PrintHex((ulong)compSize);
                    Syscall.PrintString("\r\n");
                }
            }
        }

        private static void PrintHex(ulong v)
        {
            var buf = stackalloc char[18];
            buf[0] = '0';
            buf[1] = 'x';
            for (int i = 0; i < 16; i++)
            {
                int nib = (int)((v >> ((15 - i) * 4)) & 0xF);
                buf[2 + i] = (char)(nib < 10 ? '0' + nib : 'a' + nib - 10);
            }
            Syscall.Print(buf, 18);
        }
    }
}