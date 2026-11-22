// Minimal OSLANG AST for M4.1 Ring 3 Proof
// All structs - no reference types to avoid GC write barriers

namespace Osb.Tests
{
    internal enum StmtKind : byte
    {
        None = 0,
        VarDecl,
        Assign,
        Print,
    }

    internal enum ExprKind : byte
    {
        None = 0,
        NumberLiteral,
        StringLiteral,
        Identifier,
    }

    // Fixed-size statement - no managed arrays
    internal readonly struct Stmt
    {
        public readonly StmtKind Kind;
        public readonly string Name;        // For VarDecl: variable name; For Assign: variable name
        public readonly Expr Expr1;         // For Assign/Print: value expression
        public readonly Expr Expr2;         // For Print: optional second expression

        private Stmt(StmtKind kind, string name, Expr expr1, Expr expr2)
        {
            Kind = kind;
            Name = name;
            Expr1 = expr1;
            Expr2 = expr2;
        }

        public static Stmt MakeVarDecl(string name) => new(StmtKind.VarDecl, name, default, default);
        public static Stmt MakeAssign(string name, Expr value) => new(StmtKind.Assign, name, value, default);
        public static Stmt MakePrint(Expr expr1, Expr expr2 = default) => new(StmtKind.Print, "", expr1, expr2);
    }

    // Fixed-size expression - no managed arrays
    internal readonly struct Expr
    {
        public readonly ExprKind Kind;
        public readonly double NumberValue;
        public readonly string StringValue;
        public readonly string IdentifierName;

        private Expr(ExprKind kind, double numberValue, string stringValue, string identifierName)
        {
            Kind = kind;
            NumberValue = numberValue;
            StringValue = stringValue;
            IdentifierName = identifierName;
        }

        public static Expr MakeNumber(double value) => new(ExprKind.NumberLiteral, value, null, null);
        public static Expr MakeString(string value) => new(ExprKind.StringLiteral, 0, value, null);
        public static Expr MakeIdentifier(string name) => new(ExprKind.Identifier, 0, null, name);
    }

    // Function declaration with fixed-size body (max 8 statements)
    internal readonly struct FunctionDecl
    {
        public readonly string Name;
        public readonly Stmt Stmt1, Stmt2, Stmt3, Stmt4;
        public readonly Stmt Stmt5, Stmt6, Stmt7, Stmt8;
        public readonly byte StmtCount;

        public FunctionDecl(string name, Stmt stmt1, Stmt stmt2, Stmt stmt3, Stmt stmt4,
            Stmt stmt5 = default, Stmt stmt6 = default, Stmt stmt7 = default, Stmt stmt8 = default, byte count = 4)
        {
            Name = name;
            Stmt1 = stmt1; Stmt2 = stmt2; Stmt3 = stmt3; Stmt4 = stmt4;
            Stmt5 = stmt5; Stmt6 = stmt6; Stmt7 = stmt7; Stmt8 = stmt8;
            StmtCount = count;
        }

        public Stmt GetStmt(int index)
        {
            return index switch
            {
                0 => Stmt1, 1 => Stmt2, 2 => Stmt3, 3 => Stmt4,
                4 => Stmt5, 5 => Stmt6, 6 => Stmt7, 7 => Stmt8,
                _ => default
            };
        }
    }

    // Program - just holds the main function
    internal readonly struct OslangProgram
    {
        public readonly FunctionDecl MainFunction;

        public OslangProgram(FunctionDecl mainFunction)
        {
            MainFunction = mainFunction;
        }
    }
}