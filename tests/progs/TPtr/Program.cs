using OsbKT;

namespace Osb.Tests
{
    // S3 + B5 proof: pointer hardening. Every pointer-taking syscall must
    // reject out-of-window pointers and never touch the buffer. The kernel
    // must survive every bad call. Prints "TPTR: PASS n=<count>" only if all
    // cases return the expected value and the kernel is still alive at the end.
    //
    // Cases (B5): below UserBase, above UserLimit, ptr+len wrap (true u64
    // wrap and a len crossing UserLimit), null pointer, 09h above UserLimit,
    // 20h with rsi = 100000 (out-of-range length), 20h with the path crossing
    // UserLimit using the BYTE rule, and the pointer-taking graphics/input
    // calls 42h/48h/50h/56h. The PASS line prints the real number of cases.
    internal static unsafe class Program
    {
        private const ulong UserBase = 0x2000000;
        private const ulong UserLimit = 0x2200000;

        private static int _cases;
        private static int _fails;

        private static void Main()
        {
            ulong below = UserBase - 0x10;         // below UserBase
            ulong above = UserLimit + 0x10;        // above UserLimit
            ulong nullPtr = 0;

            // 01h WRITE(buf RDI, len RSI): bad buf -> -1
            Check("01h below", Syscall.Raw(0x01, 0, below, 8, 0, 0), -1);
            Check("01h above", Syscall.Raw(0x01, 0, above, 8, 0, 0), -1);
            Check("01h crossing", Syscall.Raw(0x01, 0, UserLimit - 4, 8, 0, 0), -1);
            Check("01h wrap", Syscall.Raw(0x01, 0, unchecked((ulong)-1) - 8, 16, 0, 0), -1); // true u64 wrap
            Check("01h nullPtr", Syscall.Raw(0x01, 0, nullPtr, 8, 0, 0), -1);

            // 09h PRINT(string RSI): bad ptr -> -1 (start check); above limit
            Check("09h below", Syscall.Raw(0x09, 0, 0, below, 0, 0), -1);
            Check("09h above", Syscall.Raw(0x09, 0, 0, above, 0, 0), -1);
            Check("09h nullPtr", Syscall.Raw(0x09, 0, 0, nullPtr, 0, 0), -1);

            // 10h MEMINFO(out RDI): bad out -> -1
            Check("10h below", Syscall.Raw(0x10, 0, below, 0, 0, 0), -1);
            Check("10h wrap", Syscall.Raw(0x10, 0, UserLimit - 8, 0, 0, 0), -1); // 24B crosses
            Check("10h nullPtr", Syscall.Raw(0x10, 0, nullPtr, 0, 0, 0), -1);

            // 20h RESOLVE(path RDI, len RSI, out RDX): bad path or out -> 0
            Check("20h path-below", Syscall.Raw(0x20, 0, below, 8, UserBase, 0), 0);
            Check("20h out-nullPtr", Syscall.Raw(0x20, 0, UserBase, 8, nullPtr, 0), 0);
            Check("20h out-wrap", Syscall.Raw(0x20, 0, UserBase, 8, UserLimit - 4, 0), 0);
            Check("20h len-100000", Syscall.Raw(0x20, 0, UserBase, 100000, UserBase + 0x1000, 0), 0); // rsi > 260
            Check("20h path-cross", Syscall.Raw(0x20, 0, UserLimit - 4, 8, UserBase, 0), 0); // byte rule crosses

            // 21h DIRNEXT(ino RDI, idx RSI, out RDX, cap RCX): bad out -> 0
            Check("21h out-nullPtr", Syscall.Raw(0x21, 0, 2, 0, nullPtr, 8), 0);
            Check("21h out-wrap", Syscall.Raw(0x21, 0, 2, 0, UserLimit - 4, 0x100), 0);

            // 22h GETCWD(buf RDI, cap RSI): bad buf -> -1
            Check("22h below", Syscall.Raw(0x22, 0, below, 64, 0, 0), -1);
            Check("22h wrap", Syscall.Raw(0x22, 0, UserLimit - 4, 0x100, 0, 0), -1);
            Check("22h nullPtr", Syscall.Raw(0x22, 0, nullPtr, 64, 0, 0), -1);

            // 23h GETARGS(buf RDI, cap RSI): bad buf -> -1
            Check("23h below", Syscall.Raw(0x23, 0, below, 64, 0, 0), -1);
            Check("23h wrap", Syscall.Raw(0x23, 0, UserLimit - 4, 0x100, 0, 0), -1);
            Check("23h nullPtr", Syscall.Raw(0x23, 0, nullPtr, 64, 0, 0), -1);

            // 6Ah SPAWNPATH(req RDI): any bad req block -> -1 (B4: EFAULT)
            Check("6Ah req-nullPtr", Syscall.Raw(0x6A, 0, nullPtr, 0, 0, 0), -1);
            Check("6Ah req-below", Syscall.Raw(0x6A, 0, below, 0, 0, 0), -1);
            Check("6Ah req-wrap", Syscall.Raw(0x6A, 0, UserLimit - 4, 0, 0, 0), -1);

            // 42h GFX_INFO(ptr RDI): bad out -> -1
            Check("42h below", Syscall.Raw(0x42, 0, below, 0, 0, 0), -1);
            Check("42h wrap", Syscall.Raw(0x42, 0, UserLimit - 4, 0, 0, 0), -1);

            // 48h TEXT(xy RDI, ptr RSI, len RDX): bad ptr -> -1
            Check("48h below", Syscall.Raw(0x48, 0, 0, below, 8, 0), -1);
            Check("48h wrap", Syscall.Raw(0x48, 0, 0, UserLimit - 4, 8, 0), -1);

            // 50h MOUSE(ptr RDI): bad out -> -1
            Check("50h below", Syscall.Raw(0x50, 0, below, 0, 0, 0), -1);
            Check("50h wrap", Syscall.Raw(0x50, 0, UserLimit - 4, 0, 0, 0), -1);

            // 56h TIME(ptr RDI): bad out -> -1
            Check("56h below", Syscall.Raw(0x56, 0, below, 0, 0, 0), -1);
            Check("56h wrap", Syscall.Raw(0x56, 0, UserLimit - 2, 0, 0, 0), -1); // 3B crosses

            // If we got here, the kernel survived every bad pointer.
            if (_fails == 0)
            {
                Syscall.PrintString("TPTR: PASS n=");
                var nbuf = stackalloc char[8];
                int nn = Fmt.U32(nbuf, (uint)_cases);
                Syscall.Print(nbuf, nn);
                Syscall.PrintString("\r\n");
                Syscall.Exit(0);
            }
            else
            {
                Syscall.PrintString("TPTR: FAIL (");
                var buf = stackalloc char[8];
                int n = Fmt.U32(buf, (uint)_fails);
                Syscall.Print(buf, n);
                Syscall.PrintString(" of ");
                int nc = Fmt.U32(buf, (uint)_cases);
                Syscall.Print(buf, nc);
                Syscall.PrintString(")\r\n");
                Syscall.Exit(1);
            }
        }

        private static void Check(string name, int got, int want)
        {
            _cases++;
            if (got != want)
            {
                _fails++;
                Syscall.PrintString("TPTR: ");
                Syscall.PrintString(name);
                Syscall.PrintString(" expected ");
                PrintInt(want);
                Syscall.PrintString(" got ");
                PrintInt(got);
                Syscall.PrintString("\r\n");
            }
        }

        private static void PrintInt(int v)
        {
            var buf = stackalloc char[16];
            int n = Fmt.S32(buf, v);
            Syscall.Print(buf, n);
        }
    }
}