using Osb.Shell.Userspace;
using OsbKT;

namespace Osb.Shell.Unit
{
    // LineEditor behavior: typing, erase/redraw, arrows/Home/End/Delete,
    // history, Ctrl+C, CRLF swallowing, and the 80-column wrap (serial + VGA
    // cursor). Each test drives a scripted GetChar stream and inspects the
    // returned line plus the 80x25 console model.
    internal static class LineEditorTests
    {
        private const string Prompt = "OSB> ";

        public static void Run()
        {
            Typing();
            BackspaceAtEnd();
            BackspaceMidLine();
            ArrowsHomeEnd();
            DeleteChar();
            HistoryNav();
            CtrlCAborts();
            CrlfSwallow();
            Wrap();
            WrapBackspace();
        }

        private static void Typing()
        {
            string line = Program.ReadLine(Program.Cat(Program.Str("ABC\r")), out int code);
            Program.Eq("ABC", line, "typing: line");
            Program.EqInt(3, code, "typing: length");
            Program.Eq(Prompt + "ABC", Syscall.Console.RowText(0), "typing: screen");
            Program.EqInt(1, Syscall.Console.Row, "typing: row after Enter");
            Program.EqInt(0, Syscall.Console.Col, "typing: col after Enter");
        }

        private static void BackspaceAtEnd()
        {
            string line = Program.ReadLine(Program.Cat(Program.Str("AB\bC\r")), out int code);
            Program.Eq("AC", line, "backspace-at-end: line");
            Program.EqInt(2, code, "backspace-at-end: length");
            Program.Eq(Prompt + "AC", Syscall.Console.RowText(0), "backspace-at-end: screen");
        }

        private static void BackspaceMidLine()
        {
            // ABC, Left (caret after B), Backspace removes B -> redraw.
            string line = Program.ReadLine(
                Program.Cat(Program.Str("ABC"), Program.Esc('D'), Program.Str("\b\r")),
                out int code);
            Program.Eq("AC", line, "backspace-mid: line");
            Program.EqInt(2, code, "backspace-mid: length");
            Program.Eq(Prompt + "AC", Syscall.Console.RowText(0), "backspace-mid: screen");
        }

        private static void ArrowsHomeEnd()
        {
            string line = Program.ReadLine(
                Program.Cat(Program.Str("AC"), Program.Esc('H'), Program.Str("X"),
                            Program.Esc('F'), Program.Str("Y\r")),
                out int code);
            Program.Eq("XACY", line, "home/end: line");
            Program.EqInt(4, code, "home/end: length");
        }

        private static void DeleteChar()
        {
            // ABC, Home, Right (caret after A), Delete removes B.
            string line = Program.ReadLine(
                Program.Cat(Program.Str("ABC"), Program.Esc('H'), Program.Esc('C'),
                            Program.Esc('3', '~'), Program.Str("\r")),
                out int code);
            Program.Eq("AC", line, "delete: line");
            Program.EqInt(2, code, "delete: length");
        }

        private static void HistoryNav()
        {
            History.NavReset();
            Program.ReadLine(Program.Cat(Program.Str("HIST1\r")), out _);
            Program.PushHistory("HIST1");
            Program.ReadLine(Program.Cat(Program.Str("HIST2\r")), out _);
            Program.PushHistory("HIST2");

            string line = Program.ReadLine(Program.Cat(Program.Esc('A'), Program.Str("\r")), out _);
            Program.Eq("HIST2", line, "history: up");
            line = Program.ReadLine(Program.Cat(Program.Esc('A'), Program.Esc('A'), Program.Str("\r")), out _);
            Program.Eq("HIST1", line, "history: up twice");
            line = Program.ReadLine(
                Program.Cat(Program.Esc('A'), Program.Esc('A'), Program.Esc('B'), Program.Str("\r")),
                out _);
            Program.Eq("HIST2", line, "history: down");
        }

        private static void CtrlCAborts()
        {
            string line = Program.ReadLine(Program.Cat(Program.Str("ABC"), new[] { -2 }), out int code);
            Program.Check(line == null, "ctrl-c: returns null line");
            Program.EqInt(-1, code, "ctrl-c: code");
        }

        private static void CrlfSwallow()
        {
            // A CR sets the skip flag; the LF that follows on the NEXT read is
            // swallowed instead of ending an empty line.
            Program.ReadLine(Program.Cat(Program.Str("AB\r")), out _);
            string line = Program.ReadLine(Program.Cat(Program.Str("\nC\r")), out _);
            Program.Eq("C", line, "crlf: leading LF swallowed");
        }

        private static void Wrap()
        {
            string s = "0123456789" + new string('x', 70);   // 80 chars
            Program.ReadLine(Program.Cat(Program.Str(s + "\r")), out int code);
            Program.EqInt(80, code, "wrap: line length");
            Program.Eq(Prompt + s.Substring(0, 75), Syscall.Console.RowText(0), "wrap: row 0");
            Program.Eq(s.Substring(75), Syscall.Console.RowText(1), "wrap: row 1");
            Program.EqInt(2, Syscall.Console.Row, "wrap: row after Enter");
        }

        private static void WrapBackspace()
        {
            string s = "0123456789" + new string('x', 70);   // 80 chars
            string line = Program.ReadLine(Program.Cat(Program.Str(s + "\b\r")), out int code);
            Program.Eq(s.Substring(0, 79), line, "wrap-backspace: line");
            Program.EqInt(79, code, "wrap-backspace: length");
            Program.Eq(Prompt + s.Substring(0, 75), Syscall.Console.RowText(0), "wrap-backspace: row 0");
            Program.Eq(s.Substring(75, 4), Syscall.Console.RowText(1), "wrap-backspace: row 1");
        }
    }
}