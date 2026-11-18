using System;

namespace Osb.Shell.Unit
{
    // 80x25 text console with the kernel's wrap-aware backspace semantics.
    // LineEditor's redraw uses "\b" to walk back to the caret, and the kernel
    // treats "\b" at column 0 as "previous row, last column"; that is the
    // behavior the golden VGA captures exercise, so the model mirrors it.
    public sealed class ConsoleModel
    {
        public const int Cols = 80;
        public const int Rows = 25;

        private readonly char[] _screen = new char[Cols * Rows];

        public int Row { get; private set; }
        public int Col { get; private set; }

        public ConsoleModel()
        {
            Clear();
        }

        public void Clear()
        {
            for (int i = 0; i < _screen.Length; i++)
                _screen[i] = ' ';
            Row = 0;
            Col = 0;
        }

        public void Put(char c)
        {
            if (c == '\r') { Col = 0; return; }
            if (c == '\n') { NewLine(); return; }
            if (c == '\b')
            {
                if (Col > 0) Col--;
                else if (Row > 0) { Row--; Col = Cols - 1; }
                return;
            }
            if (c < ' ') return;

            _screen[Row * Cols + Col] = c;
            Col++;
            if (Col == Cols)
            {
                Col = 0;
                NewLine();
            }
        }

        private void NewLine()
        {
            Row++;
            if (Row == Rows)
                Scroll();
        }

        private void Scroll()
        {
            for (int r = 1; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                    _screen[(r - 1) * Cols + c] = _screen[r * Cols + c];
            for (int c = 0; c < Cols; c++)
                _screen[(Rows - 1) * Cols + c] = ' ';
            Row = Rows - 1;
        }

        public string RowText(int r)
        {
            var s = new char[Cols];
            for (int c = 0; c < Cols; c++)
                s[c] = _screen[r * Cols + c];
            return new string(s).TrimEnd();
        }

        public string ScreenText()
        {
            var s = new System.Text.StringBuilder();
            for (int r = 0; r < Rows; r++)
            {
                if (r > 0) s.Append('\n');
                s.Append(RowText(r));
            }
            return s.ToString();
        }
    }
}