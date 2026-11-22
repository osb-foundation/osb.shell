// Minimal OSLANG Interpreter for M4.1 Ring 3 Proof
// All structs - no reference types

using OsbKT;

namespace Osb.Tests
{
    internal struct Interpreter
    {
        private Scope _globals;

        public void Execute(OslangProgram program)
        {
            ExecuteFunction(program.MainFunction);
        }

        private void ExecuteFunction(FunctionDecl func)
        {
            for (int i = 0; i < func.StmtCount; i++)
            {
                ExecuteStatement(func.GetStmt(i));
            }
        }

        private void ExecuteStatement(Stmt stmt)
        {
            switch (stmt.Kind)
            {
                case StmtKind.VarDecl:
                    _globals.DeclareLocal(stmt.Name);
                    break;

                case StmtKind.Assign:
                    _globals.Assign(stmt.Name, Evaluate(stmt.Expr1));
                    break;

                case StmtKind.Print:
                    ExecutePrint(stmt.Expr1, stmt.Expr2);
                    break;
            }
        }

        private void ExecutePrint(Expr expr1, Expr expr2)
        {
            var value1 = Evaluate(expr1);
            string text1 = value1.ToString();
            Syscall.PrintString(text1);

            if (expr2.Kind != ExprKind.None)
            {
                var value2 = Evaluate(expr2);
                string text2 = value2.ToString();
                Syscall.PrintString(text2);
            }

            Syscall.PrintString("\r\n");
        }

        private OslangValue Evaluate(Expr expr)
        {
            return expr.Kind switch
            {
                ExprKind.NumberLiteral => OslangValue.Number(expr.NumberValue),
                ExprKind.StringLiteral => OslangValue.String(expr.StringValue),
                ExprKind.Identifier => _globals.Lookup(expr.IdentifierName),
                _ => OslangValue.NullValue,
            };
        }
    }
}