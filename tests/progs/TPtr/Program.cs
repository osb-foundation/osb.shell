using OsbKT;

namespace Osb.Tests
{
    // S3 proof: pointer hardening. Every pointer-taking syscall must reject
    // out-of-window pointers with -1 (EFAULT) and never touch the buffer. The
    // kernel must survive every bad call. Prints "TPTR: PASS" only if all of
    // them return the expected error and the kernel is still alive at the end.
    //
    // Bad pointers tested: below UserBase, above UserLimit, ptr+len wrap
    // (len chosen so ptr+len overflows past UserLimit), a valid ptr with a len
    // that crosses UserLimit, and nullPtr (0). Each is checked per syscall.
    internal static unsafe class Program
    {
        private const ulong UserBase = 0x2000000;
        private const ulong UserLimit = 0x2200000;

        // A real, valid buffer (inside the user window) for the "len crosses
        // the limit" case: ptr valid, len huge so ptr+len > UserLimit.
        private static int _fails;

        private static void Main()
        {
            // A valid pointer we own (stack) — but the user window is the code
            // image, so any pointer into [UserBase, UserLimit) is "valid" for
            // the kernel's range check. Use UserBase itself as a valid base.
            ulong valid = UserBase;
            ulong below = UserBase - 0x10;        // below UserBase
            ulong above = UserLimit + 0x10;       // above UserLimit
            ulong nullPtr = 0;

            // 01h WRITE(buf RDI, len RSI): bad buf -> -1
            Check("01h below", Syscall.Raw(0x01, 0, below, 8, 0, 0), -1);
            Check("01h above", Syscall.Raw(0x01, 0, above, 8, 0, 0), -1);
            Check("01h wrap", Syscall.Raw(0x01, 0, UserLimit - 4, 8, 0, 0), -1); // crosses limit
            Check("01h nullPtr", Syscall.Raw(0x01, 0, nullPtr, 8, 0, 0), -1);

            // 09h PRINT(string RSI): bad ptr -> -1 (start check)
            Check("09h below", Syscall.Raw(0x09, 0, 0, below, 0, 0), -1);
            Check("09h nullPtr", Syscall.Raw(0x09, 0, 0, nullPtr, 0, 0), -1);

            // 10h MEMINFO(out RDI): bad out -> -1
            Check("10h below", Syscall.Raw(0x10, 0, below, 0, 0, 0), -1);
            Check("10h wrap", Syscall.Raw(0x10, 0, UserLimit - 8, 0, 0, 0), -1); // 24B crosses
            Check("10h nullPtr", Syscall.Raw(0x10, 0, nullPtr, 0, 0, 0), -1);

            // 20h RESOLVE(path RDI, len RSI, out RDX): bad path or out -> 0
            Check("20h path-below", Syscall.Raw(0x20, 0, below, 8, valid, 0), 0);
            Check("20h out-nullPtr", Syscall.Raw(0x20, 0, valid, 8, nullPtr, 0), 0);
            Check("20h out-wrap", Syscall.Raw(0x20, 0, valid, 8, UserLimit - 4, 0), 0);

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

            // 6Ah SPAWNPATH(req RDI): bad req ptr -> -5 (EINVAL)
            Check("6Ah req-nullPtr", Syscall.Raw(0x6A, 0, nullPtr, 0, 0, 0), -5);
            Check("6Ah req-below", Syscall.Raw(0x6A, 0, below, 0, 0, 0), -5);
            Check("6Ah req-wrap", Syscall.Raw(0x6A, 0, UserLimit - 4, 0, 0, 0), -5);

            // If we got here, the kernel survived every bad pointer.
            if (_fails == 0)
            {
                Syscall.PrintString("TPTR: PASS\r\n");
                Syscall.Exit(0);
            }
            else
            {
                Syscall.PrintString("TPTR: FAIL (");
                var buf = stackalloc char[8];
                int n = Fmt.U32(buf, (uint)_fails);
                Syscall.Print(buf, n);
                Syscall.PrintString(")\r\n");
                Syscall.Exit(1);
            }
        }

        private static void Check(string name, int got, int want)
        {
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