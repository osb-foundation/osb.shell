using System;
using System.Collections.Generic;
using System.Text;

namespace OsbKT
{
    // Host-side stand-in for the INT 21h wrappers used by LineEditor and
    // Completion. GetChar() is scripted by the test; the file calls are
    // answered from a tiny in-memory ext4-like tree; PutChar/Print write to
    // the ConsoleModel the tests inspect. Only the methods the linked shell
    // sources call are defined.
    public static unsafe class Syscall
    {
        public static Osb.Shell.Unit.ConsoleModel Console = new Osb.Shell.Unit.ConsoleModel();

        private static int[] _input = Array.Empty<int>();
        private static int _pos;
        private static string _cwd = "A:\\OSB\\ROOT";

        public static void SetInput(params int[] seq)
        {
            _input = seq;
            _pos = 0;
        }

        public static void SetCwd(string cwd)
        {
            _cwd = cwd;
        }

        public static int GetChar()
        {
            return _pos < _input.Length ? _input[_pos++] : -1;
        }

        public static void Sleep(ulong ticks)
        {
        }

        public static void PutChar(char c) => Console.Put(c);

        public static void Print(char* buf, int len)
        {
            for (int i = 0; i < len; i++)
                Console.Put(buf[i]);
        }

        public static void PrintString(string s)
        {
            foreach (char c in s)
                Console.Put(c);
        }

        public static int GetCwd(byte* buf, int cap)
        {
            byte[] b = Encoding.ASCII.GetBytes(_cwd);
            int n = b.Length < cap ? b.Length : cap;
            for (int i = 0; i < n; i++)
                buf[i] = b[i];
            return n;
        }

        public static bool ResolvePath(char* path, int len, out uint ino, out ushort mode, out ushort uid, out ulong size)
        {
            ino = 0;
            mode = 0;
            uid = 0;
            size = 0;
            string p = PtrToString(path, len).ToUpperInvariant();
            switch (p)
            {
                case "A:\\OSB": ino = 3; break;
                case "A:\\OSB\\ROOT": ino = 2; break;
                case "A:\\OSB\\ROOT\\TRASH": ino = 4; break;
                case "\\": ino = 1; break;
                default: return false;
            }
            mode = 0x4000;   // directory
            return true;
        }

        private sealed class Dir
        {
            public string[] Names;
            public byte[] Types;
        }

        private static readonly Dictionary<uint, Dir> _dirs = new Dictionary<uint, Dir>
        {
            [1] = new Dir { Names = new[] { "OSB" }, Types = new byte[] { 2 } },
            [2] = new Dir
            {
                Names = new[] { "BIN", "TRASH", "README.TXT", "HISTORY.TXT" },
                Types = new byte[] { 2, 2, 1, 1 }
            },
            [3] = new Dir
            {
                Names = new[] { "ROOT", "BIN", "CONF" },
                Types = new byte[] { 2, 2, 2 }
            },
            [4] = new Dir { Names = Array.Empty<string>(), Types = Array.Empty<byte>() }
        };

        public static bool DirNext(uint dirIno, int index, byte* outBuf, int cap)
        {
            if (!_dirs.TryGetValue(dirIno, out Dir dir))
                return false;
            if (index < 0 || index >= dir.Names.Length)
                return false;
            byte[] nb = Encoding.ASCII.GetBytes(dir.Names[index]);
            if (6 + nb.Length > cap)
                return false;
            uint ino = dirIno * 100u + (uint)index + 1u;
            outBuf[0] = (byte)ino;
            outBuf[1] = (byte)(ino >> 8);
            outBuf[2] = (byte)(ino >> 16);
            outBuf[3] = (byte)(ino >> 24);
            outBuf[4] = dir.Types[index];
            outBuf[5] = (byte)nb.Length;
            for (int i = 0; i < nb.Length; i++)
                outBuf[6 + i] = nb[i];
            return true;
        }

        private static string PtrToString(char* p, int len)
        {
            var sb = new StringBuilder(len);
            for (int i = 0; i < len; i++)
                sb.Append(p[i]);
            return sb.ToString();
        }
    }
}