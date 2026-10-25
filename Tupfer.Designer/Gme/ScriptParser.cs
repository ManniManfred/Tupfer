using System.Globalization;

namespace Tupfer.Designer.Gme;

/// <summary>Register vor der Auflösung: entweder Nummer ($3) oder Name ($zug).</summary>
public sealed record Register(ushort? Number, string? Name)
{
    public override string ToString() => Number is { } n ? $"${n}" : $"${Name}";
}

/// <summary>Operand: Konstante oder Register.</summary>
public sealed record Value(ushort Constant, Register? Reg)
{
    public static Value Const(ushort c) => new(c, null);
}

public sealed record Condition(Value Left, byte[] Op, Value Right);

/// <summary>
/// Ein Befehl im GME-Format: Register (Wort), zwei (oder mehr) Opcode-Bytes, Operand.
/// <see cref="JumpTarget"/> ist gesetzt, solange ein benannter Sprung J(name) noch nicht aufgelöst ist.
/// </summary>
public sealed record Command(Register? Reg, byte[] Op, Value Arg, string? JumpTarget = null);

public sealed record ScriptLine(List<Condition> Conditions, List<Command> Commands, List<string> Media);

public class ScriptParseException : Exception
{
    public ScriptParseException(string message) : base(message) { }
}

/// <summary>
/// Parser für die Skriptsprache von tttool (Portierung von parseLine/parseCommands aus TipToiYaml.hs).
/// </summary>
public class ScriptParser
{
    const string OpLetters = ":!#%&*+./<=>?@\\^|-~";

    static readonly Dictionary<string, byte[]> CondOps = new()
    {
        ["=="] = new byte[] { 0xF9, 0xFF },
        [">"] = new byte[] { 0xFA, 0xFF },
        ["<"] = new byte[] { 0xFB, 0xFF },
        [">="] = new byte[] { 0xFD, 0xFF },
        ["<="] = new byte[] { 0xFE, 0xFF },
        ["/="] = new byte[] { 0xFF, 0xFF },
        ["!="] = new byte[] { 0xFF, 0xFF },
    };

    static readonly Dictionary<string, byte[]> ArithOps = new()
    {
        [":="] = new byte[] { 0xF9, 0xFF },
        ["+="] = new byte[] { 0xF0, 0xFF },
        ["-="] = new byte[] { 0xF1, 0xFF },
        ["*="] = new byte[] { 0xF2, 0xFF },
        ["/="] = new byte[] { 0xF3, 0xFF },
        ["%="] = new byte[] { 0xF4, 0xFF },
        ["&="] = new byte[] { 0xF5, 0xFF },
        ["|="] = new byte[] { 0xF6, 0xFF },
        ["^="] = new byte[] { 0xF7, 0xFF },
    };

    readonly string _s;
    int _pos;

    ScriptParser(string s)
    {
        _s = s;
    }

    public static ScriptLine ParseLine(string line)
    {
        var p = new ScriptParser(line);
        p.SkipWs();
        var conds = new List<Condition>();
        while (true)
        {
            int save = p._pos;
            var c = p.TryCondition();
            if (c == null)
            {
                p._pos = save;
                break;
            }
            conds.Add(c);
        }
        var (cmds, media) = p.Commands();
        return new ScriptLine(conds, cmds, media);
    }

    /// <summary>Parst die Initialisierung, z. B. "$turn := 1 $x := 5".</summary>
    public static List<(Register Reg, ushort Value)> ParseInit(string init)
    {
        var p = new ScriptParser(init);
        var result = new List<(Register, ushort)>();
        p.SkipWs();
        while (!p.AtEnd)
        {
            var r = p.Reg() ?? throw p.Error("Register erwartet");
            if (p.Op() != ":=")
                throw p.Error("„:=“ erwartet");
            result.Add((r, p.Number() ?? throw p.Error("Zahl erwartet")));
        }
        return result;
    }

    /// <summary>Parst eine kommagetrennte Liste von Audio-Namen (z. B. welcome).</summary>
    public static List<string> ParsePlayList(string s)
    {
        var p = new ScriptParser(s);
        p.SkipWs();
        var list = new List<string>();
        if (p.AtEnd)
            return list;
        while (true)
        {
            list.Add(p.AudioRef());
            if (!p.Accept(','))
                break;
        }
        if (!p.AtEnd)
            throw p.Error("Komma oder Zeilenende erwartet");
        return list;
    }

    bool AtEnd => _pos >= _s.Length;

    char Peek => AtEnd ? '\0' : _s[_pos];

    ScriptParseException Error(string what) =>
        new($"{what} an Position {_pos + 1}: „{(AtEnd ? "Zeilenende" : _s[_pos..])}“");

    void SkipWs()
    {
        while (!AtEnd && char.IsWhiteSpace(_s[_pos]))
            _pos++;
    }

    bool Accept(char c)
    {
        if (Peek != c)
            return false;
        _pos++;
        SkipWs();
        return true;
    }

    void Expect(char c)
    {
        if (!Accept(c))
            throw Error($"„{c}“ erwartet");
    }

    bool AcceptWord(string w)
    {
        if (string.CompareOrdinal(_s, _pos, w, 0, w.Length) != 0)
            return false;
        _pos += w.Length;
        SkipWs();
        return true;
    }

    /// <summary>Liest einen Operator (maximale Folge von Operatorzeichen, wie Parsec reservedOp).</summary>
    string? Op()
    {
        int start = _pos;
        while (!AtEnd && OpLetters.Contains(_s[_pos]))
            _pos++;
        if (_pos == start)
            return null;
        var op = _s[start.._pos];
        SkipWs();
        return op;
    }

    ushort? Number()
    {
        if (AtEnd || !char.IsAsciiDigit(Peek))
            return null;
        int start = _pos;
        long value;
        if (Peek == '0' && _pos + 1 < _s.Length && (_s[_pos + 1] is 'x' or 'X' or 'o' or 'O'))
        {
            bool hex = _s[_pos + 1] is 'x' or 'X';
            _pos += 2;
            int digitsStart = _pos;
            while (!AtEnd && (hex ? char.IsAsciiHexDigit(Peek) : Peek is >= '0' and <= '7'))
                _pos++;
            if (_pos == digitsStart)
                throw Error("Ziffern erwartet");
            value = Convert.ToInt64(_s[digitsStart.._pos], hex ? 16 : 8);
        }
        else
        {
            while (!AtEnd && char.IsAsciiDigit(Peek))
                _pos++;
            value = long.Parse(_s[start.._pos], CultureInfo.InvariantCulture);
        }
        SkipWs();
        // tttool wandelt mit fromIntegral, d. h. modulo 2^16
        return unchecked((ushort)value);
    }

    Register? Reg()
    {
        if (Peek != '$')
            return null;
        _pos++;
        if (Number() is { } n)
            return new Register(n, null);
        int start = _pos;
        while (!AtEnd && (char.IsLetterOrDigit(Peek) || Peek == '_'))
            _pos++;
        if (_pos == start)
            throw Error("Registername erwartet");
        var name = _s[start.._pos];
        SkipWs();
        return new Register(null, name);
    }

    Value? TryValue()
    {
        if (Reg() is { } r)
            return new Value(0, r);
        if (Number() is { } n)
            return Value.Const(n);
        return null;
    }

    Value ValueOrFail() => TryValue() ?? throw Error("Wert erwartet");

    Condition? TryCondition()
    {
        var left = TryValue();
        if (left == null)
            return null;
        var op = Op();
        if (op == null || !CondOps.TryGetValue(op, out var code))
            return null;
        var right = TryValue();
        if (right == null || !Accept('?'))
            return null;
        return new Condition(left, code, right);
    }

    string AudioRef()
    {
        if (Peek == '"')
        {
            _pos++;
            var sb = new System.Text.StringBuilder();
            while (!AtEnd && Peek != '"')
            {
                if (Peek == '\\' && _pos + 1 < _s.Length)
                    _pos++;
                sb.Append(_s[_pos++]);
            }
            Expect('"');
            return sb.ToString();
        }
        int start = _pos;
        while (!AtEnd && (char.IsLetterOrDigit(Peek) || Peek is '_' or '.'))
            _pos++;
        if (_pos == start)
            throw Error("Audio-Name erwartet");
        var name = _s[start.._pos];
        SkipWs();
        return name;
    }

    (List<Command>, List<string>) Commands()
    {
        var cmds = new List<Command>();
        var media = new List<string>();
        var none = Value.Const(0);

        void Add(Register? reg, byte a, byte b, Value v, string? jump = null) =>
            cmds.Add(new Command(reg, new[] { a, b }, v, jump));

        while (!AtEnd)
        {
            if (Reg() is { } reg)
            {
                var op = Op();
                if (op == null || !ArithOps.TryGetValue(op, out var code))
                    throw Error("Rechenoperator (:=, +=, -=, …) erwartet");
                cmds.Add(new Command(reg, code, ValueOrFail()));
            }
            else if (AcceptWord("Neg"))
            {
                Expect('(');
                var r = Reg() ?? throw Error("Register erwartet");
                Expect(')');
                Add(r, 0xF8, 0xFF, none);
            }
            else if (Peek == '?')
            {
                _pos++;
                SkipWs();
                Expect('(');
                var r = Reg() ?? throw Error("Register erwartet");
                Expect(',');
                var v = ValueOrFail();
                Expect(')');
                Expect('(');
                var bytes = new List<byte>();
                while (!AtEnd && char.IsAsciiHexDigit(Peek))
                {
                    int start = _pos;
                    while (!AtEnd && char.IsAsciiHexDigit(Peek))
                        _pos++;
                    bytes.Add(unchecked((byte)Convert.ToInt64(_s[start.._pos], 16)));
                    SkipWs();
                }
                if (bytes.Count == 0)
                    throw Error("Hex-Bytes erwartet");
                Expect(')');
                cmds.Add(new Command(r, bytes.ToArray(), v));
            }
            else if (Peek == 'P')
            {
                _pos++;
                bool all = false, star = false, flip = false;
                if (Peek == 'A') { all = true; _pos++; }
                if (Peek == '*') { star = true; _pos++; }
                if (Peek == '?') { flip = true; _pos++; }
                SkipWs();
                Expect('(');
                var names = new List<string> { AudioRef() };
                while (Accept(','))
                    names.Add(AudioRef());
                Expect(')');
                var variantArg = none;
                if (Accept('('))
                {
                    variantArg = ValueOrFail();
                    Expect(')');
                }
                int i = media.Count, n = names.Count;
                byte first = (byte)i, last = (byte)(i + n - 1);
                switch (all, star, flip)
                {
                    case (false, false, false) when n == 1: Add(null, 0xE8, 0xFF, Value.Const((ushort)i)); break;
                    case (false, false, false): Add(null, 0x00, 0xFC, Value.Const((ushort)(last | first << 8))); break;
                    case (false, false, true) when n == 1: Add(null, 0xA1, 0xFF, Value.Const((ushort)i)); break;
                    case (false, false, true): throw Error("P? spielt genau eine Audiodatei");
                    case (true, false, false): Add(null, 0x00, 0xFB, Value.Const((ushort)(last | first << 8))); break;
                    case (false, true, false): Add(null, 0xE0, 0xFF, variantArg); break;
                    case (true, true, false): Add(null, 0xE1, 0xFF, variantArg); break;
                    default: throw Error("P? kann nicht mit A oder * kombiniert werden");
                }
                media.AddRange(names);
            }
            else if (AcceptWord("CT"))
            {
                Add(null, 0xFF, 0xFE, Value.Const(0xFFFF));
            }
            else if (Accept('C'))
            {
                Add(null, 0xFF, 0xFA, Value.Const(0xFFFF));
            }
            else if (Accept('J'))
            {
                Expect('(');
                if (TryValue() is { } target)
                    Add(null, 0xFF, 0xF8, target);
                else
                {
                    int start = _pos;
                    while (!AtEnd && (char.IsLetterOrDigit(Peek) || Peek == '_'))
                        _pos++;
                    if (_pos == start)
                        throw Error("Skriptname erwartet");
                    var name = _s[start.._pos];
                    SkipWs();
                    Add(null, 0xFF, 0xF8, none, name);
                }
                Expect(')');
            }
            else if (Accept('T'))
            {
                Expect('(');
                var r = Reg() ?? throw Error("Register erwartet");
                Expect(',');
                var v = ValueOrFail();
                Expect(')');
                Add(r, 0x00, 0xFF, v);
            }
            else if (AcceptWord("AT"))
            {
                Expect('(');
                var n = Number() ?? throw Error("Zahl erwartet");
                Expect(')');
                Add(null, 0x00, 0xFE, Value.Const(n));
            }
            else if (Accept('G'))
            {
                Expect('(');
                var n = Number() ?? throw Error("Zahl erwartet");
                Expect(')');
                Add(null, 0x00, 0xFD, Value.Const(n));
            }
            else if (AcceptWord("SP"))
            {
                Expect('(');
                var p = Number() ?? throw Error("Zahl erwartet");
                if (p > 8)
                    throw Error("Soundprofil außerhalb des Bereichs (0..8)");
                var v = none;
                if (Accept(','))
                    v = ValueOrFail();
                Expect(')');
                Add(null, (byte)(0xE0 + p), 0xFE, v);
            }
            else
            {
                throw Error("Unbekannter Befehl");
            }
        }
        return (cmds, media);
    }
}
