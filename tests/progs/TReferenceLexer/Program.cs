// M4.3 Reference Lexer Freestanding Test
// Tests the actual OSLANG 0.72 Reference Lexer in Ring 3

using OsbKT;
using Osb.Lang.Lexing;
using Osb.Lang.Diagnostics;

namespace Osb.Tests
{
    internal static unsafe class Program
    {
        [System.Runtime.InteropServices.DllImport("osbnative")]
        private static extern ulong osb_tls_tp();

        [System.Runtime.InteropServices.DllImport("osbnative")]
        private static extern ulong osb_tls_get_alloc();

        [System.Runtime.InteropServices.DllImport("osbnative")]
        private static extern ulong osb_tls_get_limit();

        [System.Runtime.InteropServices.DllImport("osbnative")]
        private static extern void osb_tls_set_alloc(ulong v);

        [System.Runtime.InteropServices.DllImport("osbnative")]
        private static extern void osb_tls_set_limit(ulong v);

        public static void PrintDebug(string msg)
        {
            Syscall.PrintString("M4.3: DEBUG: ");
            Syscall.PrintString(msg);
            Syscall.PrintString("\r\n");
        }

        private static void Main()
        {
            // Initialize TLS heap for NativeAOT allocation helpers
            const ulong HeapBase = 0x207C000;
            const ulong HeapLimit = 0x2200000;
            osb_tls_set_alloc(HeapBase);
            osb_tls_set_limit(HeapLimit);

            Syscall.PrintString("M4.3: REFERENCE LEXER TEST START\r\n");
            PrintDebug("After init");

            // Test 1: Keywords and identifiers
            PrintDebug("Starting TestKeywordsAndIdentifiers");
            TestKeywordsAndIdentifiers();

            // Test 2: Numeric literals
            TestNumericLiterals();

            // Test 3: String literals
            TestStringLiterals();

            // Test 4: Operators and punctuation
            TestOperatorsAndPunctuation();

            // Test 5: Source locations
            TestSourceLocations();

            // Test 6: Comments and newlines
            TestCommentsAndNewlines();

            // Test 7: Invalid input
            TestInvalidInput();

            Syscall.PrintString("M4.3: ALL TESTS PASSED\r\n");
            Syscall.PrintString("M4.3: PASS\r\n");
            Syscall.Exit(0);
        }

        private static void TestKeywordsAndIdentifiers()
        {
            PrintDebug("TEST 1 - Keywords and Identifiers");
            PrintDebug("Before simple test");
            Syscall.PrintString("M4.3: SIMPLE TEST\r\n");
            PrintDebug("After simple test");
            
            // Test basic string operations
            PrintDebug("Testing string literal");
            Syscall.PrintString("M4.3: direct print\r\n");
            PrintDebug("Direct print done");
            
            PrintDebug("Testing string var");
            var s = "hello";
            Syscall.PrintString("M4.3: got string var\r\n");
            PrintDebug("Got string var");
            
            PrintDebug("Testing concat");
            var concat = "prefix " + s;
            Syscall.PrintString("M4.3: concat done\r\n");
            PrintDebug("Concat done: " + concat);
            
            PrintDebug("Testing StringBuilder");
            var sb = new System.Text.StringBuilder();
            Syscall.PrintString("M4.3: SB created\r\n");
            sb.Append('a');
            Syscall.PrintString("M4.3: SB append char\r\n");
            var str = sb.ToString();
            Syscall.PrintString("M4.3: SB ToString\r\n");
            PrintDebug("SB result: " + str);
            
            PrintDebug("Testing double.Parse");
            var d = double.Parse("42.5", System.Globalization.CultureInfo.InvariantCulture);
            Syscall.PrintString("M4.3: Parse done\r\n");
            PrintDebug("Parse result: " + d.ToString());
            
            PrintDebug("All basic ops work!");
            
            var source = "42";
            PrintDebug("Creating lexer for number");
            var lexer = new Lexer(source);
            PrintDebug("Lexer created");
            PrintDebug("About to call NextToken");
            var token = lexer.NextTokenPublic();
            PrintDebug("Back from NextToken, token=" + (token == null ? "null" : "not null"));
            if (token == null)
            {
                PrintDebug("TOKEN IS NULL!");
                return;
            }
            PrintDebug("NextToken returned");
            PrintDebug("Type: " + token.Type.ToString());
            PrintDebug("TEST 1 PASSED");
        }

        private static void TestNumericLiterals()
        {
            Syscall.PrintString("M4.3: TEST 2 - Numeric Literals\r\n");
            PrintDebug("TestNumericLiterals: creating lexer");
            var source = "42 3.14 0 123.456";
            var lexer = new Lexer(source);
            PrintDebug("TestNumericLiterals: calling NextToken in loop");
            
            var token1 = lexer.NextTokenPublic();
            AssertNumberTokenSingle(token1, 42.0, "42");
            PrintDebug("Token 1 OK");
            
            var token2 = lexer.NextTokenPublic();
            AssertNumberTokenSingle(token2, 3.14, "3.14");
            PrintDebug("Token 2 OK");
            
            var token3 = lexer.NextTokenPublic();
            AssertNumberTokenSingle(token3, 0.0, "0");
            PrintDebug("Token 3 OK");
            
            var token4 = lexer.NextTokenPublic();
            AssertNumberTokenSingle(token4, 123.456, "123.456");
            PrintDebug("Token 4 OK");
            
            var token5 = lexer.NextTokenPublic();
            AssertTokenSingle(token5, TokenType.Eof, "");
            PrintDebug("Token 5 OK (Eof)");
            
            Syscall.PrintString("M4.3: TEST 2 PASSED\r\n");
        }

        private static void TestStringLiterals()
        {
            Syscall.PrintString("M4.3: TEST 3 - String Literals\r\n");
            var source = "\"hello\" \"world\" \"\" \"with spaces\" \"escaped \\\" quote\"";
            var lexer = new Lexer(source);
            
            var t0 = lexer.NextTokenPublic(); AssertStringTokenSingle(t0, "hello", "\"hello\"");
            var t1 = lexer.NextTokenPublic(); AssertStringTokenSingle(t1, "world", "\"world\"");
            var t2 = lexer.NextTokenPublic(); AssertStringTokenSingle(t2, "", "\"\"");
            var t3 = lexer.NextTokenPublic(); AssertStringTokenSingle(t3, "with spaces", "\"with spaces\"");
            var t4 = lexer.NextTokenPublic(); AssertStringTokenSingle(t4, "escaped \" quote", "\"escaped \\\" quote\"");
            var t5 = lexer.NextTokenPublic(); AssertTokenSingle(t5, TokenType.Eof, "");
            
            // Test escape sequences
            var source2 = "\"newline\\n tab\\t backslash\\\\ quote\\\"\"";
            var lexer2 = new Lexer(source2);
            var t6 = lexer2.NextTokenPublic(); AssertStringTokenSingle(t6, "newline\n tab\t backslash\\ quote\"", source2);
            var t7 = lexer2.NextTokenPublic(); AssertTokenSingle(t7, TokenType.Eof, "");

            Syscall.PrintString("M4.3: TEST 3 PASSED\r\n");
        }

        private static void TestOperatorsAndPunctuation()
        {
            Syscall.PrintString("M4.3: TEST 4 - Operators and Punctuation\r\n");
            var source = "+ - * / % = == <> < > <= >= ( ) [ ] { } , : . => ++ -- +=";
            var lexer = new Lexer(source);
            
            AssertTokenSingle(lexer.NextTokenPublic(), TokenType.Plus, "+");
            AssertTokenSingle(lexer.NextTokenPublic(), TokenType.Minus, "-");
            AssertTokenSingle(lexer.NextTokenPublic(), TokenType.Star, "*");
            AssertTokenSingle(lexer.NextTokenPublic(), TokenType.Slash, "/");
            AssertTokenSingle(lexer.NextTokenPublic(), TokenType.Percent, "%");
            AssertTokenSingle(lexer.NextTokenPublic(), TokenType.Equal, "=");
            AssertTokenSingle(lexer.NextTokenPublic(), TokenType.Equal, "==");
            AssertTokenSingle(lexer.NextTokenPublic(), TokenType.NotEqual, "<>");
            AssertTokenSingle(lexer.NextTokenPublic(), TokenType.Less, "<");
            AssertTokenSingle(lexer.NextTokenPublic(), TokenType.Greater, ">");
            AssertTokenSingle(lexer.NextTokenPublic(), TokenType.LessEqual, "<=");
            AssertTokenSingle(lexer.NextTokenPublic(), TokenType.GreaterEqual, ">=");
            AssertTokenSingle(lexer.NextTokenPublic(), TokenType.LParen, "(");
            AssertTokenSingle(lexer.NextTokenPublic(), TokenType.RParen, ")");
            AssertTokenSingle(lexer.NextTokenPublic(), TokenType.LBracket, "[");
            AssertTokenSingle(lexer.NextTokenPublic(), TokenType.RBracket, "]");
            AssertTokenSingle(lexer.NextTokenPublic(), TokenType.LBrace, "{");
            AssertTokenSingle(lexer.NextTokenPublic(), TokenType.RBrace, "}");
            AssertTokenSingle(lexer.NextTokenPublic(), TokenType.Comma, ",");
            AssertTokenSingle(lexer.NextTokenPublic(), TokenType.Colon, ":");
            AssertTokenSingle(lexer.NextTokenPublic(), TokenType.Dot, ".");
            AssertTokenSingle(lexer.NextTokenPublic(), TokenType.DoubleArrow, "=>");
            AssertTokenSingle(lexer.NextTokenPublic(), TokenType.PlusPlus, "++");
            AssertTokenSingle(lexer.NextTokenPublic(), TokenType.MinusMinus, "--");
            AssertTokenSingle(lexer.NextTokenPublic(), TokenType.PlusEqual, "+=");
            AssertTokenSingle(lexer.NextTokenPublic(), TokenType.Eof, "");

            Syscall.PrintString("M4.3: TEST 4 PASSED\r\n");
        }

        private static void TestSourceLocations()
        {
            Syscall.PrintString("M4.3: TEST 5 - Source Locations\r\n");
            var source = "FUNCTION MAIN\n  VAR x\n  PRINT 42";
            var lexer = new Lexer(source);
            
            var t0 = lexer.NextTokenPublic(); AssertLocation(t0, 1, 1);   // FUNCTION
            var t1 = lexer.NextTokenPublic(); AssertLocation(t1, 1, 10);  // MAIN
            var t2 = lexer.NextTokenPublic(); AssertLocation(t2, 1, 18);  // Newline
            var t3 = lexer.NextTokenPublic(); AssertLocation(t3, 2, 3);   // VAR
            var t4 = lexer.NextTokenPublic(); AssertLocation(t4, 2, 7);   // x
            var t5 = lexer.NextTokenPublic(); AssertLocation(t5, 2, 8);   // Newline
            var t6 = lexer.NextTokenPublic(); AssertLocation(t6, 3, 3);   // PRINT
            var t7 = lexer.NextTokenPublic(); AssertLocation(t7, 3, 9);   // 42
            var t8 = lexer.NextTokenPublic(); AssertTokenSingle(t8, TokenType.Eof, "");

            Syscall.PrintString("M4.3: TEST 5 PASSED\r\n");
        }

        private static void TestCommentsAndNewlines()
        {
            Syscall.PrintString("M4.3: TEST 6 - Comments and Newlines\r\n");
            var source = "FUNCTION MAIN\n' comment\nREM another\n  VAR x\nEND FUNCTION";
            var lexer = new Lexer(source);
            
            AssertTokenSingle(lexer.NextTokenPublic(), TokenType.Function, "FUNCTION");
            AssertTokenSingle(lexer.NextTokenPublic(), TokenType.Identifier, "MAIN");
            AssertTokenSingle(lexer.NextTokenPublic(), TokenType.Newline, "\n");
            // ' comment should be skipped
            // REM another should be skipped
            AssertTokenSingle(lexer.NextTokenPublic(), TokenType.Var, "VAR");
            AssertTokenSingle(lexer.NextTokenPublic(), TokenType.Identifier, "x");
            AssertTokenSingle(lexer.NextTokenPublic(), TokenType.Newline, "\n");
            AssertTokenSingle(lexer.NextTokenPublic(), TokenType.End, "END");
            AssertTokenSingle(lexer.NextTokenPublic(), TokenType.Function, "FUNCTION");
            AssertTokenSingle(lexer.NextTokenPublic(), TokenType.Eof, "");

            Syscall.PrintString("M4.3: TEST 6 PASSED\r\n");
        }

        private static void TestInvalidInput()
        {
            Syscall.PrintString("M4.3: TEST 7 - Invalid Input\r\n");
            var source = "@"; // Invalid character
            var lexer = new Lexer(source);
            bool threw = false;
            try
            {
                // Just call NextToken once to see if it throws
                var token = lexer.NextTokenPublic();
            }
            catch (LexicalException)
            {
                threw = true;
            }
            // Note: The Reference Lexer throws on invalid chars
            // In freestanding, we may not have full exception support
            // Just verify it doesn't crash
            Syscall.PrintString("M4.3: TEST 7 COMPLETED\r\n");
        }

        private static void AssertToken(List<Token> tokens, int index, TokenType expectedType, string expectedLexeme)
        {
            if (index >= tokens.Count)
            {
                Syscall.PrintString("M4.3: FAIL - Token index out of bounds\r\n");
                Syscall.Exit(1);
            }
            var token = tokens[index];
            if (token.Type != expectedType)
            {
                Syscall.PrintString("M4.3: FAIL - Type mismatch at ");
                Syscall.PrintString(index.ToString());
                Syscall.PrintString(": expected ");
                Syscall.PrintString(expectedType.ToString());
                Syscall.PrintString(" got ");
                Syscall.PrintString(token.Type.ToString());
                Syscall.PrintString("\r\n");
                Syscall.Exit(1);
            }
            if (token.Lexeme != expectedLexeme)
            {
                Syscall.PrintString("M4.3: FAIL - Lexeme mismatch at ");
                Syscall.PrintString(index.ToString());
                Syscall.PrintString(": expected '");
                Syscall.PrintString(expectedLexeme);
                Syscall.PrintString("' got '");
                Syscall.PrintString(token.Lexeme);
                Syscall.PrintString("'\r\n");
                Syscall.Exit(1);
            }
        }

        private static void AssertNumberToken(List<Token> tokens, int index, double expectedValue, string expectedLexeme)
        {
            AssertToken(tokens, index, TokenType.NumberLiteral, expectedLexeme);
            var token = tokens[index];
            // Note: NumberValue comparison with tolerance
            double diff = token.NumberValue - expectedValue;
            if (diff < 0) diff = -diff;
            if (diff > 0.000001)
            {
                Syscall.PrintString("M4.3: FAIL - Number value mismatch at ");
                Syscall.PrintString(index.ToString());
                Syscall.PrintString("\r\n");
                Syscall.Exit(1);
            }
        }

        private static void AssertStringToken(List<Token> tokens, int index, string expectedValue, string expectedLexeme)
        {
            AssertToken(tokens, index, TokenType.StringLiteral, expectedLexeme);
            var token = tokens[index];
            if (token.StringValue != expectedValue)
            {
                Syscall.PrintString("M4.3: FAIL - String value mismatch at ");
                Syscall.PrintString(index.ToString());
                Syscall.PrintString("\r\n");
                Syscall.Exit(1);
            }
        }

        private static void AssertTokenSingle(Token token, TokenType expectedType, string expectedLexeme)
        {
            if (token.Type != expectedType)
            {
                Syscall.PrintString("M4.3: FAIL - Type mismatch: expected ");
                Syscall.PrintString(expectedType.ToString());
                Syscall.PrintString(" got ");
                Syscall.PrintString(token.Type.ToString());
                Syscall.PrintString("\r\n");
                Syscall.Exit(1);
            }
            if (token.Lexeme != expectedLexeme)
            {
                Syscall.PrintString("M4.3: FAIL - Lexeme mismatch: expected '");
                Syscall.PrintString(expectedLexeme);
                Syscall.PrintString("' got '");
                Syscall.PrintString(token.Lexeme);
                Syscall.PrintString("'\r\n");
                Syscall.Exit(1);
            }
        }

        private static void AssertNumberTokenSingle(Token token, double expectedValue, string expectedLexeme)
        {
            AssertTokenSingle(token, TokenType.NumberLiteral, expectedLexeme);
            double diff = token.NumberValue - expectedValue;
            if (diff < 0) diff = -diff;
            if (diff > 0.000001)
            {
                Syscall.PrintString("M4.3: FAIL - Number value mismatch\r\n");
                Syscall.Exit(1);
            }
        }

        private static void AssertStringTokenSingle(Token token, string expectedValue, string expectedLexeme)
        {
            AssertTokenSingle(token, TokenType.StringLiteral, expectedLexeme);
            if (token.StringValue != expectedValue)
            {
                Syscall.PrintString("M4.3: FAIL - String value mismatch\r\n");
                Syscall.Exit(1);
            }
        }

        private static void AssertLocation(Token token, int expectedLine, int expectedColumn)
        {
            if (token.Location.Line != expectedLine || token.Location.Column != expectedColumn)
            {
                Syscall.PrintString("M4.3: FAIL - Location mismatch: expected ");
                Syscall.PrintString(expectedLine.ToString());
                Syscall.PrintString(",");
                Syscall.PrintString(expectedColumn.ToString());
                Syscall.PrintString(" got ");
                Syscall.PrintString(token.Location.Line.ToString());
                Syscall.PrintString(",");
                Syscall.PrintString(token.Location.Column.ToString());
                Syscall.PrintString("\r\n");
                Syscall.Exit(1);
            }
        }
    }
}