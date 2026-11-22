// Minimal OSLANG Parser for M4.1 Ring 3 Proof
// Streaming parser - no managed array allocations, uses structs

using OsbKT;

namespace Osb.Tests
{
    internal sealed class Parser
    {
        private readonly Lexer _lexer;
        private Token _current;

        public Parser(Lexer lexer)
        {
            _lexer = lexer;
            _current = _lexer.NextToken();
        }

        public OslangProgram Parse()
        {
            SkipNewlines();
            Expect(TokenType.Function);
            Expect(TokenType.Identifier);
            string funcName = _lexer.GetTokenText(_current);
            Advance();
            Expect(TokenType.LParen);
            Advance();
            Expect(TokenType.RParen);
            Advance();
            SkipNewlines();

            // Parse statements directly into structs
            var stmt1 = ParseStatement();
            SkipNewlines();
            var stmt2 = ParseStatement();
            SkipNewlines();
            var stmt3 = ParseStatement();
            SkipNewlines();
            var stmt4 = ParseStatement();
            SkipNewlines();

            Expect(TokenType.End);
            Advance();
            Expect(TokenType.Function);
            Advance();

            var func = new FunctionDecl(funcName, stmt1, stmt2, stmt3, stmt4, default, default, default, default, 4);
            return new OslangProgram(func);
        }

        private Stmt ParseStatement()
        {
            if (Check(TokenType.Var))
            {
                Advance(); // VAR
                Expect(TokenType.Identifier);
                string name = _lexer.GetTokenText(_current);
                Advance();
                return Stmt.MakeVarDecl(name);
            }

            if (Check(TokenType.Print))
            {
                Advance(); // PRINT
                var expr1 = ParseExpression();
                var expr2 = ParseExpressionOpt();
                
                if (expr2.Kind != ExprKind.None)
                {
                    return Stmt.MakePrint(expr1, expr2);
                }
                else
                {
                    return Stmt.MakePrint(expr1);
                }
            }

            // Assignment: identifier = expression
            if (Check(TokenType.Identifier) && PeekNext() == TokenType.Assign)
            {
                string name = _lexer.GetTokenText(_current);
                Advance(); // identifier
                Advance(); // =
                var value = ParseExpression();
                return Stmt.MakeAssign(name, value);
            }

            // Skip unexpected
            Advance();
            return Stmt.MakeVarDecl("");
        }

        private Expr ParseExpression()
        {
            if (Check(TokenType.Number))
            {
                Advance();
                double value = ParseNumber(_current, _lexer);
                return Expr.MakeNumber(value);
            }

            if (Check(TokenType.String))
            {
                Advance();
                string value = _lexer.GetTokenText(_current);
                return Expr.MakeString(value);
            }

            if (Check(TokenType.Identifier))
            {
                Advance();
                string name = _lexer.GetTokenText(_current);
                return Expr.MakeIdentifier(name);
            }

            // Fallback
            Advance();
            return Expr.MakeNumber(0);
        }

        private Expr ParseExpressionOpt()
        {
            if (Check(TokenType.Number))
            {
                Advance();
                double value = ParseNumber(_current, _lexer);
                return Expr.MakeNumber(value);
            }

            if (Check(TokenType.String))
            {
                Advance();
                string value = _lexer.GetTokenText(_current);
                return Expr.MakeString(value);
            }

            if (Check(TokenType.Identifier))
            {
                Advance();
                string name = _lexer.GetTokenText(_current);
                return Expr.MakeIdentifier(name);
            }

            return default;
        }

        private bool Check(TokenType type) => _current.Type == type;

        private TokenType PeekNext()
        {
            return _current.Type;
        }

        private void Advance()
        {
            _current = _lexer.NextToken();
        }

        private void Expect(TokenType type)
        {
            if (!Check(type))
            {
                return;
            }
            Advance();
        }

        private void SkipNewlines()
        {
            while (Check(TokenType.Newline))
            {
                Advance();
            }
        }

        private static double ParseNumber(Token token, Lexer lexer)
        {
            string text = lexer.GetTokenText(token);
            double result = 0;
            int len = text.Length;
            for (int i = 0; i < len; i++)
            {
                char c = text[i];
                if (c >= '0' && c <= '9')
                {
                    result = result * 10 + (c - '0');
                }
            }
            return result;
        }
    }
}