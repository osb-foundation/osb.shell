// O8.23 M2 — OSLANG NativeAOT Ring 3 Entry Adapter
// Minimal diagnostic entry point proving the OSLANG-derived native image
// loads and its entry adapter executes in Ring 3.
// Does NOT initialize the full OSLANG engine or interpreter.
// Uses the existing OSB Ring 3 process contract (crt0.S, user.ld, OSBX).
// Initializes TLS for NativeAOT allocation helpers, prints diagnostic, exits.

using OsbKT;

namespace Osb.Tests
{
    internal static unsafe class Program
    {
        // TLS access functions from probe.S (freestanding NativeAOT helpers)
        [System.Runtime.InteropServices.DllImport("osbnative")]
        private static extern ulong osb_tls_tp();

        [System.Runtime.InteropServices.DllImport("osbnative")]
        private static extern ulong osb_tls_get_alloc();

        [System.Runtime.InteropServices.DllImport("osbnative")]
        private static extern ulong osb_tls_get_limit();

        [System.Runtime.InteropServices.DllImport("osbnative")]
        private static extern void osb_tls_set_alloc(ulong v);

        [System.Runtime.InteropServices.DllImport("osbnative")]
        private static extern void osb_tls_set_limit(ulong v);

        private static void Main()
        {
            // Initialize TLS heap for NativeAOT allocation helpers.
            // Heap region within the user block: [0x2108000, 0x2200000) — 512 KiB
            // (Same region as O8.3.1 Freestanding Probe and M4.1 TOslangProof)
            const ulong HeapBase = 0x2108000;
            const ulong HeapLimit = 0x2200000;

            osb_tls_set_alloc(HeapBase);
            osb_tls_set_limit(HeapLimit);

            // Verify TLS initialization
            ulong tp = osb_tls_tp();
            ulong allocPtr = osb_tls_get_alloc();
            ulong limitPtr = osb_tls_get_limit();

            // M2 Diagnostic: unique message identifying OSLANG NativeAOT entry adapter
            Syscall.PrintString("=== O8.23 M2: OSLANG NativeAOT Ring 3 Entry Adapter ===\r\n");
            Syscall.PrintString("Native image loaded by OSB loader (SPAWNPATH AH=6Ah)\r\n");
            Syscall.PrintString("Entry adapter executing in Ring 3\r\n");
            Syscall.PrintString("TLS initialized: TP=");
            PrintHex(tp);
            Syscall.PrintString(" Alloc=");
            PrintHex(allocPtr);
            Syscall.PrintString(" Limit=");
            PrintHex(limitPtr);
            Syscall.PrintString("\r\n");

            // Verify heap region matches expected values
            if (allocPtr == HeapBase && limitPtr == HeapLimit)
            {
                Syscall.PrintString("Heap region verified: [0x2108000, 0x2200000)\r\n");
            }
            else
            {
                Syscall.PrintString("Heap region MISMATCH\r\n");
                Syscall.Exit(1);
            }

            // M2 success marker
            Syscall.PrintString("O8.23_M2_ADAPTER_EXECUTED\r\n");

            // Exit with distinctive code (0x23 = 35 decimal = M2)
            Syscall.Exit(0x23);
        }

        private static void PrintHex(ulong value)
        {
            const string hexChars = "0123456789ABCDEF";
            var buf = stackalloc char[16];
            buf[0] = '0';
            buf[1] = 'x';
            for (int i = 15; i >= 2; i--)
            {
                buf[i] = hexChars[(int)(value & 0xF)];
                value >>= 4;
            }
            Syscall.Print(buf, 16);
        }
    }
}