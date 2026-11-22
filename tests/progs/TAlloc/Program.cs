using System;
using System.Runtime.InteropServices;
using OsbKT;

namespace Osb.Tests
{
    // M3.4.3 multi-task allocation-helper isolation.
    //
    // Two independently scheduled Ring 3 tasks each own a private TLS slot
    // (provisioned by the kernel in M3.4.2) and an independently initialized
    // heap region. Both call the REAL NativeAOT allocation helpers
    // (AllocFast_real.o from libRuntime.WorkstationGC.a), unmodified; only the
    // GC fallback symbols are stubbed and are not reached by the success path.
    //
    // The parent spawns a child, then the two alternate through three phases
    // separated by Sleep() (which yields to the scheduler). Every phase first
    // re-verifies every object allocated in earlier phases (header, data
    // pattern, cursor) and then allocates three new objects. A final bounds
    // test checks the bounded-allocation fallback and a zero-length test checks
    // the empty-object contract.
    internal static unsafe class Program
    {
        [DllImport("osbnative")] static extern ulong RhNewString(ulong mt, ulong len);
        [DllImport("osbnative")] static extern ulong RhpNewArrayFast(ulong mt, ulong count);
        [DllImport("osbnative")] static extern ulong RhpNewPtrArrayFast(ulong mt, ulong count);

        [DllImport("osbnative")] static extern ulong osb_tls_tp();
        [DllImport("osbnative")] static extern ulong osb_tls_get_alloc();
        [DllImport("osbnative")] static extern ulong osb_tls_get_limit();
        [DllImport("osbnative")] static extern void osb_tls_set_alloc(ulong v);
        [DllImport("osbnative")] static extern void osb_tls_set_limit(ulong v);

        // MethodTable prefix used by the real helpers: component size (WORD) at
        // +0, base size (DWORD) at +4. RhNewString and RhpNewPtrArrayFast do
        // not read it; RhpNewArrayFast reads the component size at +0.
        private struct MtBuf { public fixed byte b[32]; }
        private static MtBuf _mtInt;
        private static MtBuf _mtPtr;

        private const int MaxRecs = 16;
        private struct U64Buf { public fixed ulong v[MaxRecs]; }
        private struct I32Buf { public fixed int v[MaxRecs]; }
        private static U64Buf _rPtr, _rSize, _rMt, _rCount;
        private static I32Buf _rKind, _rPat;
        private static int _nrecs;
        private static int _fails;
        private static int _checks;

        private static ulong _heapBase;
        private static ulong _heapLimit;
        private static ulong _tp0;

        private static void Main()
        {
            var args = stackalloc byte[128];
            int alen = Syscall.GetArgs(args, 128);
            bool child = alen >= 5 && args[0] == 'c' && args[1] == 'h' &&
                         args[2] == 'i' && args[3] == 'l' && args[4] == 'd';
            if (child) Child();
            else Parent();
        }

        private static void Parent()
        {
            uint pid = Syscall.GetPid();
            _heapBase = 0x2108000;   // heap A: above every TLS slot
            _heapLimit = 0x2180000;
            osb_tls_set_alloc(_heapBase);
            osb_tls_set_limit(_heapLimit);
            _tp0 = osb_tls_tp();
            InitMt();

            int cpid = Syscall.SpawnPath("A:\\OSB\\BIN\\TAlloc.EXE", "child");
            if (cpid < 0)
            {
                Print("TALLOC: FAIL spawn=");
                PrintNum((uint)cpid);
                Print("\r\n");
                Syscall.Exit(1);
            }

            // Phase 0 runs before the child is scheduled: the parent owns the
            // CPU when it spawns, so its first allocations always come first.
            Phase(0, 0x11, "P0");
            Print("TALLOC P0 ");
            Print(_fails == 0 ? "PASS" : "FAIL");
            Print(" tp="); PrintHex(osb_tls_tp());
            Print("\r\n");
            Syscall.Sleep(20);   // yield: the child runs its phase 0

            Phase(1, 0x22, "P1");
            Print("TALLOC P1 ");
            Print(_fails == 0 ? "PASS" : "FAIL");
            Print(" tp="); PrintHex(osb_tls_tp());
            Print("\r\n");
            Syscall.Sleep(20);

            Phase(2, 0x33, "P2");
            Print("TALLOC P2 ");
            Print(_fails == 0 ? "PASS" : "FAIL");
            Print(" tp="); PrintHex(osb_tls_tp());
            Print("\r\n");

            BoundsTest();
            ZeroTest();
            CheckTp("end");

            int childCode = Syscall.Wait(cpid);
            bool ok = _fails == 0 && childCode == 0;
            Print("TALLOC: ");
            Print(ok ? "PASS" : "FAIL");
            Print(" pid="); PrintNum(pid);
            Print(" child="); PrintNum((uint)cpid);
            Print(" child_code="); PrintNum((uint)(childCode & 0xFF));
            Print(" checks="); PrintNum((uint)_checks);
            Print(" fails="); PrintNum((uint)_fails);
            Print(" tp="); PrintHex(osb_tls_tp());
            Print(" end="); PrintHex(osb_tls_get_alloc());
            Print("\r\n");
            Syscall.Exit(ok ? 0 : 1);
        }

        private static void Child()
        {
            uint pid = Syscall.GetPid();
            _heapBase = 0x2180000;   // heap B: distinct from the parent's
            _heapLimit = 0x21FFFF0;
            osb_tls_set_alloc(_heapBase);
            osb_tls_set_limit(_heapLimit);
            _tp0 = osb_tls_tp();
            InitMt();

            Phase(0, 0xAA, "C0");
            Print("TALLOC C0 ");
            Print(_fails == 0 ? "PASS" : "FAIL");
            Print(" tp="); PrintHex(osb_tls_tp());
            Print("\r\n");
            Syscall.Sleep(20);

            Phase(1, 0xBB, "C1");
            Print("TALLOC C1 ");
            Print(_fails == 0 ? "PASS" : "FAIL");
            Print(" tp="); PrintHex(osb_tls_tp());
            Print("\r\n");
            Syscall.Sleep(20);

            Phase(2, 0xCC, "C2");
            Print("TALLOC C2 ");
            Print(_fails == 0 ? "PASS" : "FAIL");
            Print(" tp="); PrintHex(osb_tls_tp());
            Print("\r\n");

            BoundsTest();
            ZeroTest();
            CheckTp("end");

            Print("TALLOC child: ");
            Print(_fails == 0 ? "PASS" : "FAIL");
            Print(" pid="); PrintNum(pid);
            Print(" checks="); PrintNum((uint)_checks);
            Print(" fails="); PrintNum((uint)_fails);
            Print(" tp="); PrintHex(osb_tls_tp());
            Print(" end="); PrintHex(osb_tls_get_alloc());
            Print("\r\n");
            Syscall.Exit(_fails == 0 ? 0 : 1);
        }

        private static void InitMt()
        {
            fixed (byte* p = _mtInt.b)
            {
                *(ushort*)(p + 0) = 4;    // component size (int)
                *(uint*)(p + 4) = 24;     // base size
            }
            fixed (byte* p = _mtPtr.b)
            {
                *(ushort*)(p + 0) = 8;    // component size (pointer)
                *(uint*)(p + 4) = 24;
            }
        }

        // The kernel must restore this task's FS base on every switch-in. If it
        // does not, the task sees the other task's thread pointer after a
        // Sleep(), which is exactly what this check detects.
        private static void CheckTp(string tag)
        {
            ulong tp = osb_tls_tp();
            _checks++;
            if (tp != _tp0 || tp < 0x2100000 || tp >= 0x2108000)
            {
                _fails++;
                Print("TALLOC FAIL tp ");
                Print(tag);
                Print(" tp="); PrintHex(tp);
                Print(" want="); PrintHex(_tp0);
                Print("\r\n");
            }
        }

        // Allocate a string, an int array and a pointer array, verify each one,
        // stamp its payload with `pattern`, and remember it for later phases.
        private static void Phase(int phase, byte pattern, string tag)
        {
            CheckTp(tag);
            VerifyAll();

            ulong sLen = (ulong)(2 + phase);     // 2, 3, 4
            ulong aCount = (ulong)(3 + phase);   // 3, 4, 5
            ulong pCount = (ulong)(2 + phase);   // 2, 3, 4

            ulong mtInt, mtPtr;
            fixed (byte* p = _mtInt.b) mtInt = (ulong)p;
            fixed (byte* p = _mtPtr.b) mtPtr = (ulong)p;

            ulong before = osb_tls_get_alloc();
            ulong sp = RhNewString(mtInt, sLen);
            ulong ss = osb_tls_get_alloc() - before;
            CheckOne("string", 0, sp, ss, mtInt, sLen);

            before = osb_tls_get_alloc();
            ulong ap = RhpNewArrayFast(mtInt, aCount);
            ulong asz = osb_tls_get_alloc() - before;
            CheckOne("array", 1, ap, asz, mtInt, aCount);

            before = osb_tls_get_alloc();
            ulong pp = RhpNewPtrArrayFast(mtPtr, pCount);
            ulong psz = osb_tls_get_alloc() - before;
            CheckOne("ptrarray", 2, pp, psz, mtPtr, pCount);

            WriteData(sp, 12, 2 * sLen, pattern);
            WriteData(ap, 16, 4 * aCount, pattern);
            WriteData(pp, 16, 8 * pCount, pattern);

            AddRec(sp, ss, mtInt, sLen, 0, pattern);
            AddRec(ap, asz, mtInt, aCount, 1, pattern);
            AddRec(pp, psz, mtPtr, pCount, 2, pattern);
        }

        private static ulong WantSize(int kind, ulong count)
        {
            if (kind == 0) return (29 + 2 * count) & ~7UL;   // string
            if (kind == 1) return (31 + 4 * count) & ~7UL;   // int array
            return 24 + 8 * count;                           // pointer array
        }

        private static void CheckOne(string name, int kind, ulong p, ulong size,
            ulong mt, ulong count)
        {
            _checks++;
            ulong want = WantSize(kind, count);
            bool ok = p != 0;
            if (ok)
            {
                if (size != want) ok = false;
                if (*(ulong*)p != mt) ok = false;
                if (*(uint*)(p + 8) != (uint)count) ok = false;
                if (osb_tls_get_alloc() > _heapLimit) ok = false;
            }
            if (!ok)
            {
                _fails++;
                Print("TALLOC FAIL ");
                Print(name);
                Print(" p="); PrintHex(p);
                Print(" size="); PrintHex(size);
                Print(" want="); PrintHex(want);
                Print(" mt="); PrintHex(ok ? *(ulong*)p : 0);
                Print(" count="); PrintNum(ok ? *(uint*)(p + 8) : 0);
                Print("\r\n");
            }
        }

        private static void WriteData(ulong p, int off, ulong len, byte pattern)
        {
            byte* d = (byte*)(p + (ulong)off);
            for (ulong i = 0; i < len; i++) d[i] = pattern;
        }

        // Re-check every object allocated in earlier phases: MethodTable,
        // length/count, payload pattern, and that the cursor still covers it.
        private static void VerifyAll()
        {
            int n = _nrecs;
            fixed (ulong* ptr = _rPtr.v)
            fixed (ulong* size = _rSize.v)
            fixed (ulong* mt = _rMt.v)
            fixed (ulong* count = _rCount.v)
            fixed (int* kind = _rKind.v)
            fixed (int* pat = _rPat.v)
            {
                for (int i = 0; i < n; i++)
                {
                    ulong p = ptr[i], sz = size[i], m = mt[i], c = count[i];
                    int k = kind[i];
                    byte wantPat = (byte)pat[i];
                    _checks++;
                    bool ok = true;
                    if (p == 0 || *(ulong*)p != m) ok = false;
                    else if (*(uint*)(p + 8) != (uint)c) ok = false;
                    else if (osb_tls_get_alloc() < p + sz) ok = false;
                    else
                    {
                        int off = k == 0 ? 12 : 16;
                        ulong dlen = k == 0 ? 2 * c : k == 1 ? 4 * c : 8 * c;
                        byte* d = (byte*)(p + (ulong)off);
                        for (ulong j = 0; j < dlen; j++)
                            if (d[j] != wantPat) { ok = false; break; }
                    }
                    if (!ok)
                    {
                        _fails++;
                        Print("TALLOC FAIL verify#");
                        PrintNum((uint)i);
                        Print(" p="); PrintHex(p);
                        Print("\r\n");
                    }
                }
            }
        }

        private static void AddRec(ulong p, ulong sz, ulong m, ulong c, int k, byte pat)
        {
            int i = _nrecs;
            fixed (ulong* a = _rPtr.v) a[i] = p;
            fixed (ulong* a = _rSize.v) a[i] = sz;
            fixed (ulong* a = _rMt.v) a[i] = m;
            fixed (ulong* a = _rCount.v) a[i] = c;
            fixed (int* a = _rKind.v) a[i] = k;
            fixed (int* a = _rPat.v) a[i] = pat;
            _nrecs++;
        }

        // Bounded allocation: a 16-byte heap cannot satisfy any of the three
        // requests, so each helper must take its fallback path (stub returns
        // NULL) and leave the cursor untouched. This does not invoke the GC or
        // the fatal allocation path.
        private static void BoundsTest()
        {
            int fails0 = _fails;
            ulong savedAlloc = osb_tls_get_alloc();
            ulong savedLimit = osb_tls_get_limit();
            osb_tls_set_alloc(_heapBase);
            osb_tls_set_limit(_heapBase + 16);

            ulong mtInt;
            fixed (byte* mp = _mtInt.b) mtInt = (ulong)mp;

            _checks++;
            ulong p = RhNewString(mtInt, 5);       // 32 bytes needed
            if (p != 0 || osb_tls_get_alloc() != _heapBase)
            { _fails++; Print("TALLOC FAIL bounds-string\r\n"); }

            _checks++;
            p = RhpNewArrayFast(mtInt, 3);          // 40 bytes needed
            if (p != 0 || osb_tls_get_alloc() != _heapBase)
            { _fails++; Print("TALLOC FAIL bounds-array\r\n"); }

            _checks++;
            p = RhpNewPtrArrayFast(mtInt, 3);       // 48 bytes needed
            if (p != 0 || osb_tls_get_alloc() != _heapBase)
            { _fails++; Print("TALLOC FAIL bounds-ptr\r\n"); }

            osb_tls_set_alloc(savedAlloc);
            osb_tls_set_limit(savedLimit);
            Print("TALLOC bounds: ");
            Print(_fails == fails0 ? "PASS" : "FAIL");
            Print("\r\n");
        }

        // Zero-length contract: every helper must return a 24-byte object with
        // the MethodTable at +0 and length/count 0 at +8.
        private static void ZeroTest()
        {
            int fails0 = _fails;
            ulong savedAlloc = osb_tls_get_alloc();
            ulong savedLimit = osb_tls_get_limit();
            ulong mtInt;
            fixed (byte* mp = _mtInt.b) mtInt = (ulong)mp;

            _checks++;
            ulong a = RhNewString(mtInt, 0);
            bool ok = a != 0 && osb_tls_get_alloc() - a == 24 &&
                      *(ulong*)a == mtInt && *(uint*)(a + 8) == 0;

            _checks++;
            ulong b = RhpNewArrayFast(mtInt, 0);
            ok = ok && b != 0 && osb_tls_get_alloc() - b == 24 &&
                 *(ulong*)b == mtInt && *(uint*)(b + 8) == 0;

            _checks++;
            ulong c = RhpNewPtrArrayFast(mtInt, 0);
            ok = ok && c != 0 && osb_tls_get_alloc() - c == 24 &&
                 *(ulong*)c == mtInt && *(uint*)(c + 8) == 0;

            if (!ok) { _fails++; Print("TALLOC FAIL zero\r\n"); }

            osb_tls_set_alloc(savedAlloc);
            osb_tls_set_limit(savedLimit);
            Print("TALLOC zero: ");
            Print(_fails == fails0 ? "PASS" : "FAIL");
            Print("\r\n");
        }

        private static void Print(string s) { Syscall.PrintString(s); }

        private static void PrintNum(uint v)
        {
            var buf = stackalloc char[16];
            int n = Fmt.U32(buf, v);
            Syscall.Print(buf, n);
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