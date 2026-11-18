using System;
using System.Collections.Generic;
using Osb.Shell.Userspace;
using OsbKT;

namespace Osb.Shell.Unit
{
    // Tiny dependency-free test runner (net10.0 console app). `dotnet run`
    // executes the suites; a non-zero exit means a failure.
    internal static class Program
    {
        public static int Pass;
        public static int Fail;

        public static void Check(bool ok, string msg)
        {
            if (ok)
            {
                Pass++;
            }
            else
            {
                Fail++;
                Console.Error.WriteLine("FAIL: " + msg);
            }
        }

        public static void Eq(string expected, string actual, string msg)
        {
            if (expected == actual)
            {
                Pass++;
            }
            else
            {
                Fail++;
                Console.Error.WriteLine("FAIL: " + msg + " expected " + Q(expected) + " got " + Q(actual));
            }
        }

        public static void EqInt(int expected, int actual, string msg)
        {
            Eq(expected.ToString(), actual.ToString(), msg);
        }

        private static string Q(string s)
        {
            return "\"" + (s ?? "<null>") + "\"";
        }

        public static int[] Str(string s)
        {
            var l = new List<int>();
            foreach (char c in s)
                l.Add(c);
            return l.ToArray();
        }

        public static int[] Cat(params int[][] parts)
        {
            var l = new List<int>();
            foreach (var p in parts)
                l.AddRange(p);
            return l.ToArray();
        }

        // ESC [ ... (the CSI prefix the kernel synthesizes for arrows etc.)
        public static int[] Esc(params int[] after)
        {
            return Cat(new[] { 0x1B }, new[] { (int)'[' }, after);
        }

        // Run one line through LineEditor with a prompt "OSB> ". Returns the
        // line (null on Ctrl+C) and sets code to Read's return value.
        public static string ReadLine(int[] input, out int code)
        {
            Syscall.Console.Clear();
            Syscall.SetInput(input);
            unsafe
            {
                char* buf = stackalloc char[256];
                char* prompt = stackalloc char[5];
                prompt[0] = 'O';
                prompt[1] = 'S';
                prompt[2] = 'B';
                prompt[3] = '>';
                prompt[4] = ' ';
                int len = LineEditor.Read(prompt, 5, buf, 256, true, true);
                code = len;
                return len < 0 ? null : new string(buf, 0, len);
            }
        }

        public static unsafe void PushHistory(string s)
        {
            char* b = stackalloc char[s.Length + 1];
            for (int i = 0; i < s.Length; i++)
                b[i] = s[i];
            b[s.Length] = '\0';
            History.Push(b, s.Length);
        }

        public static int Main()
        {
            Commands.Init();
            LineEditorTests.Run();
            CompletionTests.Run();
            Console.WriteLine("unit: pass=" + Pass + " fail=" + Fail);
            return Fail == 0 ? 0 : 1;
        }
    }
}