using System;
using System.Runtime.InteropServices;
using OsbKT;

namespace Osb.Tests
{
    // M3.4.2 Ring-3 TLS provisioning proof.
    //
    // Exercises the exact FS-relative sequence the NativeAOT linker emits:
    //
    //     mov %fs:0, %rax
    //     lea -0xD0(%rax), %rax
    //
    // through the assembly probe in probe.S. No C global and no simulated
    // pointer: every TLS value is read/written through the %fs segment.
    //
    // Parent mode (no args): spawns a second copy with args "child" (a second
    // task with its own TLS slot), lets it run, writes its own TLS values,
    // yields again, reads them back, waits for the child, and prints the
    // combined result.
    //
    // Child mode (args "child"): writes its own TLS values, yields, reads them
    // back, prints its result.
    //
    // If FS base were not restored on every switch-in, or if the TLS object
    // were shared between tasks, the parent would read zeros (or the child's
    // values) after the context switch and print FAIL.
    internal static unsafe class Program
    {
        private const ulong UserBase = 0x2000000;
        private const ulong UserLimit = 0x2200000;
        private const ulong UserStackTop = 0x2080000;
        private const ulong TlsObjectSize = 0xD0;

        [DllImport("osbnative")] private static extern ulong osb_tls_tp();
        [DllImport("osbnative")] private static extern ulong osb_tls_obj();
        [DllImport("osbnative")] private static extern ulong osb_tls_get_limit();
        [DllImport("osbnative")] private static extern ulong osb_tls_get_alloc();
        [DllImport("osbnative")] private static extern void osb_tls_set_limit(ulong v);
        [DllImport("osbnative")] private static extern void osb_tls_set_alloc(ulong v);

        private static int _fails;

        private static void Main()
        {
            var args = stackalloc byte[128];
            int alen = Syscall.GetArgs(args, 128);
            bool child = alen >= 5 && args[0] == 'c' && args[1] == 'h' &&
                         args[2] == 'i' && args[3] == 'l' && args[4] == 'd';

            if (child) Child();
            else Parent();
        }

        // Parent: spawn a second task, interleave, verify its own TLS survives.
        private static void Parent()
        {
            uint pid = Syscall.GetPid();

            int cpid = Syscall.SpawnPath("A:\\OSB\\BIN\\TTlsProof.EXE", "child");
            if (cpid < 0)
            {
                Fail("spawn", (ulong)cpid);
                Print("TTLS: FAIL spawn rc=");
                PrintNum((uint)cpid);
                Print("\r\n");
                Syscall.Exit(1);
            }

            // Let the child run first: it writes its own TLS values while we
            // are blocked here. This guarantees it has executed before we read
            // back our values after our own yield below.
            Syscall.Sleep(20);

            bool parentOk = CheckTls(pid);

            int childCode = Syscall.Wait(cpid);
            bool ok = parentOk && childCode == 0;

            Print("TTLS: ");
            Print(ok ? "PASS" : "FAIL");
            Print(" parent_pid=");
            PrintNum(pid);
            Print(" child_pid=");
            PrintNum((uint)cpid);
            Print(" child_code=");
            PrintNum((uint)(childCode & 0xFF));
            Print(" parent_tp=");
            PrintHex(osb_tls_tp());
            Print("\r\n");
            Syscall.Exit(ok ? 0 : 1);
        }

        // Child: write its own values, yield, read back.
        private static void Child()
        {
            uint pid = Syscall.GetPid();
            bool ok = CheckTls(pid);

            Print("TTLS child: ");
            Print(ok ? "PASS" : "FAIL");
            Print(" pid=");
            PrintNum(pid);
            Print(" tp=");
            PrintHex(osb_tls_tp());
            Print(" alloc=");
            PrintHex(osb_tls_get_alloc());
            Print(" limit=");
            PrintHex(osb_tls_get_limit());
            Print("\r\n");
            Syscall.Exit(ok ? 0 : 1);
        }

        // The full TLS check: address validity, the linker-relaxed offset,
        // write/read of both fields, and survival across a context switch.
        private static bool CheckTls(uint pid)
        {
            bool ok = true;
            ulong tp = osb_tls_tp();
            ulong obj = osb_tls_obj();

            // 1. Thread pointer is a valid user-window address and its
            //    self-pointer slot is inside the window.
            if (tp < UserBase || tp + 8 > UserLimit)
            {
                Fail("tp-range", tp);
                ok = false;
            }
            // 2. tls_CurrentThread is exactly TP - 0xD0 (the linked offset).
            if (obj != tp - TlsObjectSize)
            {
                Fail("obj-offset", obj);
                ok = false;
            }
            // 3. The object ends at TP and is 208 bytes.
            if (obj + TlsObjectSize != tp)
            {
                Fail("obj-size", obj);
                ok = false;
            }
            // 4. The object is above the stack top. The stack grows down from
            //    UserStackTop and the image lives below it, so obj >=
            //    UserStackTop proves the TLS object overlaps neither.
            if (obj < UserStackTop)
            {
                Fail("obj-stack", obj);
                ok = false;
            }

            ulong wantAlloc = 0xC0DE0000UL + pid;
            ulong wantLimit = 0xCAFE0000UL + pid;

            // 5. Write known values through FS and read them back.
            osb_tls_set_alloc(wantAlloc);
            osb_tls_set_limit(wantLimit);
            if (osb_tls_get_alloc() != wantAlloc)
            {
                Fail("alloc-write", osb_tls_get_alloc());
                ok = false;
            }
            if (osb_tls_get_limit() != wantLimit)
            {
                Fail("limit-write", osb_tls_get_limit());
                ok = false;
            }

            // 6. Yield so the other task runs (and, if FS were wrong, writes
            //    its own values into our view). Then read back.
            Syscall.Sleep(15);
            if (osb_tls_get_alloc() != wantAlloc)
            {
                Fail("alloc-after-switch", osb_tls_get_alloc());
                ok = false;
            }
            if (osb_tls_get_limit() != wantLimit)
            {
                Fail("limit-after-switch", osb_tls_get_limit());
                ok = false;
            }

            return ok;
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

        private static void Fail(string name, ulong v)
        {
            _fails++;
            Print("TTLS: FAIL ");
            Print(name);
            Print("=");
            PrintHex(v);
            Print("\r\n");
        }
    }
}