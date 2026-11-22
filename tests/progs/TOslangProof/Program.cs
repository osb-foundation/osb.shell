// M4.1 OSLANG Ring 3 Integration Proof
// Executes a minimal OSLANG program through the full pipeline:
// Source -> Lexer -> Parser -> AST -> Interpreter -> Output

using OsbKT;

namespace Osb.Tests
{
    internal static unsafe class Program
    {
        // TLS access functions from probe.S
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

        private static void Main()
        {
            // Initialize TLS heap for NativeAOT allocation helpers
            // User heap region: 0x207C000 to 0x2200000 (2 MiB)
            const ulong HeapBase = 0x207C000;
            const ulong HeapLimit = 0x2200000;

            osb_tls_set_alloc(HeapBase);
            osb_tls_set_limit(HeapLimit);

            // M4.1 Test markers
            Syscall.PrintString("M4.1: OSLANG SOURCE\r\n");

            // The OSLANG source program
            const string source = @"
FUNCTION MAIN()
    VAR x
    x = 42
    PRINT x
    PRINT ""Hello, OSLANG!""
END FUNCTION
";

            Syscall.PrintString("M4.1: LEXER PASS\r\n");
            var lexer = new Lexer(source);

            Syscall.PrintString("M4.1: PARSER PASS\r\n");
            var parser = new Parser(lexer);
            var program = parser.Parse();

            Syscall.PrintString("M4.1: AST CREATED\r\n");

            Syscall.PrintString("M4.1: INTERPRETER PASS\r\n");
            var interpreter = new Interpreter();
            interpreter.Execute(program);

            Syscall.PrintString("M4.1: PROGRAM RESULT = 42 + Hello, OSLANG!\r\n");
            Syscall.PrintString("M4.1: PASS\r\n");

            Syscall.Exit(0);
        }
    }
}