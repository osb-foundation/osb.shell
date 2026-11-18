using Osb.Shell.Userspace;
using OsbKT;

namespace Osb.Shell.Unit
{
    // Completion behavior: canonical-case command/file names, the "extended in
    // place" echo path, the ambiguous listing, the longest-common-prefix
    // extension, the directory separator, and the no-match no-op. The editor
    // integration (echo vs redraw) is checked through LineEditor.Read.
    internal static class CompletionTests
    {
        private static string _line;
        private static bool _listed;
        private static bool _appended;
        private static bool _changed;
        private static int _cur;

        public static void Run()
        {
            Commands.Init();
            FlagsCanonical();
            EditorAppendsInPlace();
            EditorRedrawsOnCase();
            EditorListsAmbiguous();
            EditorFileSingle();
            EditorFileNoMatch();
            EditorFileRoot();
            EditorEmptyPrefix();
        }

        // Direct contract: Complete() reports listed/appended/changed.
        private static void FlagsCanonical()
        {
            bool listed = Complete("HI", out _, out _, out bool appended, out bool changed);
            Program.Check(!listed, "HI: not listed");
            Program.Check(appended, "HI: appended");
            Program.Check(changed, "HI: changed (length grew)");
            Program.Eq("HISTORY", _line, "HI: canonical line");
            Program.EqInt(7, _cur, "HI: caret");
        }

        private static void EditorAppendsInPlace()
        {
            string line = Program.ReadLine(Program.Cat(Program.Str("HI\t\r")), out int code);
            Program.Eq("HISTORY", line, "editor: HI<TAB> line");
            Program.EqInt(7, code, "editor: HI<TAB> length");
            Program.Eq("OSB> HISTORY", Syscall.Console.RowText(0), "editor: HI<TAB> screen");
        }

        private static void EditorRedrawsOnCase()
        {
            string line = Program.ReadLine(Program.Cat(Program.Str("tr\t\r")), out int code);
            Program.Eq("TRASH", line, "editor: tr<TAB> line");
            Program.Eq("OSB> TRASH", Syscall.Console.RowText(0), "editor: tr<TAB> screen");
        }

        private static void EditorListsAmbiguous()
        {
            string line = Program.ReadLine(Program.Cat(Program.Str("H\t\r")), out int code);
            Program.Eq("H", line, "editor: H<TAB> line (lcp)");
            Program.EqInt(1, code, "editor: H<TAB> length");
            string screen = Syscall.Console.ScreenText();
            Program.Check(screen.Contains("HELP  HALT  HISTORY"), "editor: H<TAB> lists candidates");
            Program.Check(screen.Contains("OSB> H"), "editor: H<TAB> reprints prompt + line");
        }

        private static void EditorFileSingle()
        {
            string line = Program.ReadLine(Program.Cat(Program.Str("CD A:\\OSB\\R\t\r")), out int code);
            Program.Eq("CD A:\\OSB\\ROOT\\", line, "editor: CD A:\\OSB\\R<TAB> line");
            Program.Eq("OSB> CD A:\\OSB\\ROOT\\", Syscall.Console.RowText(0), "editor: CD A:\\OSB\\R<TAB> screen");
        }

        private static void EditorFileNoMatch()
        {
            string line = Program.ReadLine(Program.Cat(Program.Str("CD A:\\OSB\\ZZZ\t\r")), out int code);
            Program.Eq("CD A:\\OSB\\ZZZ", line, "editor: no-match line");
            Program.Eq("OSB> CD A:\\OSB\\ZZZ", Syscall.Console.RowText(0), "editor: no-match screen");
        }

        private static void EditorFileRoot()
        {
            string line = Program.ReadLine(Program.Cat(Program.Str("CD \\O\t\r")), out int code);
            Program.Eq("CD \\OSB\\", line, "editor: CD \\O<TAB> line");
            Program.Eq("OSB> CD \\OSB\\", Syscall.Console.RowText(0), "editor: CD \\O<TAB> screen");
        }

        private static void EditorEmptyPrefix()
        {
            string line = Program.ReadLine(Program.Cat(Program.Str("CD \t\r")), out int code);
            Program.Eq("CD ", line, "editor: CD <TAB> line unchanged");
            string screen = Syscall.Console.ScreenText();
            Program.Check(screen.Contains("BIN"), "editor: CD <TAB> lists BIN");
            Program.Check(screen.Contains("README.TXT"), "editor: CD <TAB> lists README.TXT");
        }

        private static unsafe bool Complete(string input, out int len, out int cur, out bool appended, out bool changed)
        {
            Syscall.Console.Clear();
            char* buf = stackalloc char[256];
            for (int i = 0; i < input.Length; i++)
                buf[i] = input[i];
            len = input.Length;
            cur = len;
            bool listed = Completion.Complete(buf, ref len, ref cur, 256, out appended, out changed);
            _line = new string(buf, 0, len);
            _cur = cur;
            _listed = listed;
            _appended = appended;
            _changed = changed;
            return listed;
        }
    }
}