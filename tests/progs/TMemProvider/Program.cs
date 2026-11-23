// TMemProvider - OsbMemoryProvider Validation for P1
// Tests the OsbMemoryProvider implementation using MEMGROW syscall.

using System;
using System.Runtime.InteropServices;
using Osb.Engine.Core.Providers;
using OsbKT;

namespace Osb.Tests
{
    internal static unsafe class Program
    {
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

        private static IntPtr MakePtr(ulong addr) => new IntPtr((void*)addr);
        private static IntPtr MakePtr(long addr) => new IntPtr((void*)addr);

        private static int _fails = 0;
        private static int _checks = 0;

        public static void Main()
        {
            // Initialize TLS heap for NativeAOT
            const ulong HeapBase = 0x207C000;
            const ulong HeapLimit = 0x2200000;
            osb_tls_set_alloc(HeapBase);
            osb_tls_set_limit(HeapLimit);

            Syscall.PrintString("TMEMPROVIDER: Starting...\r\n");

            TestProviderInitialization();
            TestMEMGROWIntegration();
            TestAllocations();
            TestAlignment();
            TestOverflowProtection();
            TestReset();
            TestDispose();

            // Summary
            Syscall.PrintString("TMEMPROVIDER: Checks=");
            Syscall.PrintString(_checks.ToString());
            Syscall.PrintString(" Fails=");
            Syscall.PrintString(_fails.ToString());
            Syscall.PrintString("\r\n");

            if (_fails == 0)
            {
                Syscall.PrintString("TMEMPROVIDER: PASS\r\n");
                Syscall.Exit(0);
            }
            else
            {
                Syscall.PrintString("TMEMPROVIDER: FAIL\r\n");
                Syscall.Exit(1);
            }
        }

        private static void Check(bool condition, string name)
        {
            _checks++;
            if (!condition)
            {
                _fails++;
                Syscall.PrintString("TMEMPROVIDER: FAIL ");
                Syscall.PrintString(name);
                Syscall.PrintString("\r\n");
            }
            else
            {
                Syscall.PrintString("TMEMPROVIDER: OK ");
                Syscall.PrintString(name);
                Syscall.PrintString("\r\n");
            }
        }

        private static void TestProviderInitialization()
        {
            Syscall.PrintString("TMEMPROVIDER: Test 1 - Provider Initialization\r\n");

            IntPtr baseAddr = MakePtr(0x2010000);
            int regionSize = 192 * 1024;

            using var provider = new OsbMemoryProvider(baseAddr, regionSize, 512);

            Check(provider.BaseAddress == MakePtr(0x2010000), "BaseAddress correct");
            Check(provider.RegionSize == 192 * 1024, "RegionSize correct");
            Check(provider.ManagedRegion == null, "ManagedRegion is null");
            Check(provider.MemGrowFrames == 512, "Initial MEMGROW frames = 512");
        }

        private static void TestMEMGROWIntegration()
        {
            Syscall.PrintString("TMEMPROVIDER: Test 2 - MEMGROW Integration\r\n");

            IntPtr baseAddr = MakePtr(0x2030000);
            int regionSize = 64 * 1024;

            using var provider = new OsbMemoryProvider(baseAddr, regionSize, 0);

            int initialLimit = Syscall.MemGrow(0);
            Check(initialLimit >= 0, "Initial MEMGROW limit query");

            int growResult = Syscall.MemGrow(512);
            Check(growResult == 0, "Syscall.MemGrow(512) success");

            int newLimit = Syscall.MemGrow(0);
            Check(newLimit >= 512, "MEMGROW limit increased");
        }

        private static void TestAllocations()
        {
            Syscall.PrintString("TMEMPROVIDER: Test 3 - Allocations\r\n");

            IntPtr baseAddr = MakePtr(0x2040000);
            int regionSize = 64 * 1024;

            using var provider = new OsbMemoryProvider(baseAddr, regionSize, 0);

            IntPtr zeroPtr = provider.Alloc(0);
            Check(zeroPtr == IntPtr.Zero, "Alloc(0) returns Zero");

            IntPtr negPtr = provider.Alloc(-1);
            Check(negPtr == IntPtr.Zero, "Alloc(-1) returns Zero");

            IntPtr ptr1 = provider.Alloc(100);
            Check(ptr1 != IntPtr.Zero, "Alloc(100) succeeds");
            Check(ptr1 == MakePtr(0x2040000), "First alloc at base");

            IntPtr ptr2 = provider.Alloc(200);
            Check(ptr2 != IntPtr.Zero, "Alloc(200) succeeds");
            Check(ptr2 == MakePtr(0x2040068), "Second alloc after first (aligned)");

            IntPtr ptr3 = provider.Alloc(300);
            Check(ptr3 != IntPtr.Zero, "Alloc(300) succeeds");

            unsafe
            {
                byte* p1 = (byte*)ptr1;
                byte* p2 = (byte*)ptr2;
                byte* p3 = (byte*)ptr3;

                for (int i = 0; i < 100; i++) p1[i] = 0xAA;
                for (int i = 0; i < 200; i++) p2[i] = 0xBB;
                for (int i = 0; i < 300; i++) p3[i] = 0xCC;

                bool ok1 = true, ok2 = true, ok3 = true;
                for (int i = 0; i < 100; i++) if (p1[i] != 0xAA) ok1 = false;
                for (int i = 0; i < 200; i++) if (p2[i] != 0xBB) ok2 = false;
                for (int i = 0; i < 300; i++) if (p3[i] != 0xCC) ok3 = false;

                Check(ok1, "Data integrity ptr1");
                Check(ok2, "Data integrity ptr2");
                Check(ok3, "Data integrity ptr3");
            }

            IntPtr bigPtr = provider.Alloc(1024 * 1024);
            Check(bigPtr == IntPtr.Zero, "Large alloc fails gracefully");

            provider.Free(ptr1, 100);
            provider.Free(ptr2, 200);
            provider.Free(ptr3, 300);
            Check(true, "Free calls succeed (no-op)");
        }

        private static void TestAlignment()
        {
            Syscall.PrintString("TMEMPROVIDER: Test 4 - Alignment\r\n");

            int[] sizes = { 1, 1, 1, 15 };
            int[] aligns = { 1, 8, 16, 16 };

            for (int i = 0; i < sizes.Length; i++)
            {
                int size = sizes[i];
                int align = aligns[i];

                IntPtr baseAddr = MakePtr(0x2050000);
                int regionSize = 64 * 1024;

                using var provider = new OsbMemoryProvider(baseAddr, regionSize, 0);

                IntPtr ptr = provider.Alloc(size, align);
                Check(ptr != IntPtr.Zero, "alloc succeeds");

                if (ptr != IntPtr.Zero)
                {
                    ulong addr = (ulong)ptr;
                    ulong a = (ulong)align;
                    ulong rem = addr % a;
                    bool aligned = rem == 0;
                    Check(aligned, "address aligned");
                }
            }

            // Invalid alignment
            try
            {
                IntPtr baseAddr = MakePtr(0x2055000);
                using var provider = new OsbMemoryProvider(baseAddr, 64 * 1024, 0);
                provider.Alloc(10, 3); // Not power of two
                Check(false, "Align=3 should throw");
            }
            catch (ArgumentException)
            {
                Check(true, "Align=3 throws ArgumentException");
            }

            try
            {
                IntPtr baseAddr = MakePtr(0x2056000);
                using var provider = new OsbMemoryProvider(baseAddr, 64 * 1024, 0);
                provider.Alloc(10, 0);
                Check(false, "Align=0 should throw");
            }
            catch (ArgumentException)
            {
                Check(true, "Align=0 throws ArgumentException");
            }

            try
            {
                IntPtr baseAddr = MakePtr(0x2057000);
                using var provider = new OsbMemoryProvider(baseAddr, 64 * 1024, 0);
                provider.Alloc(10, -8);
                Check(false, "Align=-8 should throw");
            }
            catch (ArgumentException)
            {
                Check(true, "Align=-8 throws ArgumentException");
            }
        }

        private static void TestOverflowProtection()
        {
            Syscall.PrintString("TMEMPROVIDER: Test 5 - Overflow Protection\r\n");

            IntPtr baseAddr = MakePtr(0x2060000);
            int regionSize = 64 * 1024;

            using var provider = new OsbMemoryProvider(baseAddr, regionSize, 0);

            provider.Alloc(regionSize - 100);

            IntPtr ptr = provider.Alloc(200);
            Check(ptr == IntPtr.Zero, "Alloc past end fails gracefully");

            try
            {
                provider.Alloc(int.MaxValue, 8);
                Check(false, "Huge size should fail");
            }
            catch
            {
                Check(true, "Huge size handled safely");
            }
        }

        private static void TestReset()
        {
            Syscall.PrintString("TMEMPROVIDER: Test 6 - Reset\r\n");

            IntPtr baseAddr = MakePtr(0x2065000);
            int regionSize = 32 * 1024;

            using var provider = new OsbMemoryProvider(baseAddr, regionSize, 0);

            IntPtr ptr1 = provider.Alloc(1000);
            IntPtr ptr2 = provider.Alloc(2000);
            Check(ptr1 != IntPtr.Zero && ptr2 != IntPtr.Zero, "Allocations before reset");

            provider.Reset();

            IntPtr ptr3 = provider.Alloc(1000);
            Check(ptr3 == baseAddr, "After reset, alloc returns to base");
            Check(ptr3 != IntPtr.Zero, "Alloc after reset succeeds");
        }

        private static void TestDispose()
        {
            Syscall.PrintString("TMEMPROVIDER: Test 7 - Dispose\r\n");

            IntPtr baseAddr = MakePtr(0x2068000);
            int regionSize = 16 * 1024;

            var provider = new OsbMemoryProvider(baseAddr, regionSize, 0);
            IntPtr ptr = provider.Alloc(100);
            Check(ptr != IntPtr.Zero, "Alloc before dispose");

            provider.Dispose();

            try
            {
                provider.Alloc(10);
                Check(false, "Alloc after dispose should throw");
            }
            catch (ObjectDisposedException)
            {
                Check(true, "Alloc after dispose throws ObjectDisposedException");
            }

            try
            {
                provider.Free(ptr, 100);
                Check(false, "Free after dispose should throw");
            }
            catch (ObjectDisposedException)
            {
                Check(true, "Free after dispose throws ObjectDisposedException");
            }

            try
            {
                provider.Reset();
                Check(false, "Reset after dispose should throw");
            }
            catch (ObjectDisposedException)
            {
                Check(true, "Reset after dispose throws ObjectDisposedException");
            }
        }
    }
}