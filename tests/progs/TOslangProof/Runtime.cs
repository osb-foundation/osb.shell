// Minimal OSLANG Runtime Values for M4.1 Ring 3 Proof
// All structs - no reference types to avoid GC write barriers

namespace Osb.Tests
{
    internal enum ValueKind : byte
    {
        Null = 0,
        Number,
        String,
    }

    internal readonly struct OslangValue
    {
        private readonly ValueKind _kind;
        private readonly double _number;
        private readonly string _string;

        private OslangValue(ValueKind kind, double number = 0, string str = null)
        {
            _kind = kind;
            _number = number;
            _string = str;
        }

        public ValueKind Kind => _kind;
        public bool IsNull => _kind == ValueKind.Null;
        public bool IsNumber => _kind == ValueKind.Number;
        public bool IsString => _kind == ValueKind.String;

        public double AsNumber => _kind == ValueKind.Number ? _number : double.NaN;
        public string AsString => _kind == ValueKind.String ? _string : null;

        public static OslangValue NullValue => new(ValueKind.Null);
        public static OslangValue Number(double value) => new(ValueKind.Number, value);
        public static OslangValue String(string value) => new(ValueKind.String, 0, value);

        public override string ToString()
        {
            return _kind switch
            {
                ValueKind.Null => "NULL",
                ValueKind.Number => NumberToString(_number),
                ValueKind.String => _string,
                _ => "UNKNOWN",
            };
        }

        private static string NumberToString(double value)
        {
            if (double.IsNaN(value)) return "NaN";
            if (double.IsPositiveInfinity(value)) return "Infinity";
            if (double.IsNegativeInfinity(value)) return "-Infinity";

            if (value == (long)value && value >= -9007199254740991 && value <= 9007199254740991)
            {
                return ((long)value).ToString();
            }

            return value.ToString();
        }
    }

    // Variable storage - struct with value semantics
    internal struct Variable
    {
        public OslangValue Value;
    }

    // Fixed-size scope - no managed arrays
    internal struct Scope
    {
        private Variable _v0, _v1, _v2, _v3, _v4, _v5, _v6, _v7;
        private string _n0, _n1, _n2, _n3, _n4, _n5, _n6, _n7;
        private byte _count;

        public void DeclareLocal(string name)
        {
            switch (_count)
            {
                case 0: _n0 = name; _v0.Value = OslangValue.NullValue; _count = 1; break;
                case 1: _n1 = name; _v1.Value = OslangValue.NullValue; _count = 2; break;
                case 2: _n2 = name; _v2.Value = OslangValue.NullValue; _count = 3; break;
                case 3: _n3 = name; _v3.Value = OslangValue.NullValue; _count = 4; break;
                case 4: _n4 = name; _v4.Value = OslangValue.NullValue; _count = 5; break;
                case 5: _n5 = name; _v5.Value = OslangValue.NullValue; _count = 6; break;
                case 6: _n6 = name; _v6.Value = OslangValue.NullValue; _count = 7; break;
                case 7: _n7 = name; _v7.Value = OslangValue.NullValue; _count = 8; break;
            }
        }

        public void Assign(string name, OslangValue value)
        {
            for (int i = 0; i < _count; i++)
            {
                string existingName = GetName(i);
                if (string.Equals(existingName, name, System.StringComparison.OrdinalIgnoreCase))
                {
                    SetVariable(i, value);
                    return;
                }
            }
            // Not found, declare new
            DeclareLocal(name);
            SetVariable(_count - 1, value);
        }

        public OslangValue Lookup(string name)
        {
            for (int i = 0; i < _count; i++)
            {
                string existingName = GetName(i);
                if (string.Equals(existingName, name, System.StringComparison.OrdinalIgnoreCase))
                {
                    return GetVariable(i);
                }
            }
            return OslangValue.NullValue;
        }

        private string GetName(int index)
        {
            return index switch
            {
                0 => _n0, 1 => _n1, 2 => _n2, 3 => _n3,
                4 => _n4, 5 => _n5, 6 => _n6, 7 => _n7,
                _ => null
            };
        }

        private OslangValue GetVariable(int index)
        {
            return index switch
            {
                0 => _v0.Value, 1 => _v1.Value, 2 => _v2.Value, 3 => _v3.Value,
                4 => _v4.Value, 5 => _v5.Value, 6 => _v6.Value, 7 => _v7.Value,
                _ => OslangValue.NullValue
            };
        }

        private void SetVariable(int index, OslangValue value)
        {
            switch (index)
            {
                case 0: _v0.Value = value; break;
                case 1: _v1.Value = value; break;
                case 2: _v2.Value = value; break;
                case 3: _v3.Value = value; break;
                case 4: _v4.Value = value; break;
                case 5: _v5.Value = value; break;
                case 6: _v6.Value = value; break;
                case 7: _v7.Value = value; break;
            }
        }
    }
}