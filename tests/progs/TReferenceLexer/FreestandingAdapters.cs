// Freestanding adapters for Reference Lexer dependencies
// Provides ONLY the BCL types missing from MiniCoreLib that the Lexer requires

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System
{
    // Minimal Environment for TReferenceLexer - provides NewLine and GetEnvironmentVariable
    // NOT in MiniCoreLib (kernel has its own Osb.Kernel.Shell.Environment)
    public static class Environment
    {
        public static string NewLine => "\n";
        public static string GetEnvironmentVariable(string variable) => "";
    }
}

namespace System.Globalization
{
    // CultureInfo - minimal implementation for InvariantCulture (double.Parse)
    // NOT in MiniCoreLib
    public class CultureInfo : IFormatProvider
    {
        public static readonly CultureInfo InvariantCulture = new CultureInfo();

        private CultureInfo() { }

        public CompareInfo CompareInfo => CompareInfo.Invariant;
        public NumberFormatInfo NumberFormat => NumberFormatInfo.InvariantInfo;

        public object GetFormat(Type formatType) => null;
    }

    public class CompareInfo
    {
        public static readonly CompareInfo Invariant = new CompareInfo();
        private CompareInfo() { }
    }

    public class NumberFormatInfo
    {
        public static readonly NumberFormatInfo InvariantInfo = new NumberFormatInfo();
        public string NumberDecimalSeparator => ".";
        public string NumberGroupSeparator => ",";
        public int[] NumberGroupSizes => new int[] { 3 };
        public string NegativeSign => "-";
        public string PositiveSign => "+";
        private NumberFormatInfo() { }
    }
}