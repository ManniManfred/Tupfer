using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TipToy.Designer.Compiler;

class CompileException : Exception
{
    public SyntaxNode? Node { get; }

    public CompileException(SyntaxNode? node, string message) : base(message)
    {
        Node = node;
    }
}

/// <summary>
/// Übersetzt eine Teilmenge von C# in Skripte für den tiptoi-Stift (tttool-Skriptsprache).
///
/// Abbildung:
/// <list type="bullet">
/// <item>Felder (ganzzahlig oder bool) → Register, Initialwerte → init</item>
/// <item>Methoden mit [Oid] → Skripte; alle anderen Methoden werden beim Aufruf eingesetzt (inlining)</item>
/// <item>if/else/switch mit &amp;&amp;, ||, ! → Zeilen mit Bedingungen (der Stift führt die erste passende Zeile aus)</item>
/// <item>Anweisungen nach einem if, Bedingungen auf berechneten Werten und mehr als 8 Befehle
///       → Hilfsskripte, die mit J(…) angesprungen werden</item>
/// </list>
/// </summary>
public class ProgramCompiler
{
    /// <summary>In der Web-App fehlt der Übersetzer (ohne Roslyn ist sie deutlich kleiner).</summary>
    public static bool IsAvailable => true;

    public const int MaxCommandsPerLine = 8;
    const int MaxTermsPerCondition = 32;

    // ---------- Zwischendarstellung ----------

    readonly record struct Operand(ushort Value, string? Reg)
    {
        public bool IsConst => Reg == null;
        public static Operand Const(ushort v) => new(v, null);
        public static Operand Register(string r) => new(0, r);
        public override string ToString() => Reg != null ? "$" + Reg : Value.ToString(CultureInfo.InvariantCulture);
    }

    sealed record Cond(Operand Left, string Op, Operand Right)
    {
        public override string ToString() => $"{Left} {Op} {Right}?";
    }

    /// <summary>Ein Befehl als tttool-Text; <see cref="Writes"/> ist das beschriebene Register.</summary>
    sealed record Cmd(string Text, string? Writes = null);

    sealed class Line
    {
        public List<Cond> Conds { get; init; } = new();
        public List<Cmd> Cmds { get; init; } = new();
    }

    sealed class MethodCtx
    {
        public required MethodDeclarationSyntax Method { get; init; }
        public required string Prefix { get; init; }
        public Dictionary<string, string> Locals { get; init; } = new();
        public Dictionary<string, ushort> LocalConsts { get; init; } = new();

        /// <summary>Schleifenvariablen ausgerollter Schleifen (Konstanten, die nicht geändert werden dürfen).</summary>
        public HashSet<string> LoopVars { get; init; } = new();

        /// <summary>Sprungziel für return (null = Skriptende).</summary>
        public string? Ret { get; init; }

        public required IReadOnlyList<string> CallChain { get; init; }

        /// <summary>Kontext für einen Durchlauf einer ausgerollten Schleife: <paramref name="name"/> ist dort konstant.</summary>
        public MethodCtx WithLoopValue(string name, ushort value)
        {
            var child = new MethodCtx
            {
                Method = Method,
                Prefix = Prefix,
                Ret = Ret,
                CallChain = CallChain,
                Locals = new Dictionary<string, string>(Locals),
                LocalConsts = new Dictionary<string, ushort>(LocalConsts),
                LoopVars = new HashSet<string>(LoopVars) { name },
            };
            child.Locals.Remove(name);
            child.LocalConsts[name] = value;
            return child;
        }
    }

    abstract record Item(MethodCtx Ctx);

    sealed record StmtItem(StatementSyntax Stmt, MethodCtx Ctx) : Item(Ctx);

    /// <summary>Ein if/switch, dessen Bedingungen schon berechnet sind (am Anfang eines Hilfsskripts).</summary>
    sealed record PreparedIf(List<(List<List<Cond>> Dnf, List<Item> Body)> Branches, List<Item>? Else, MethodCtx Ctx) : Item(Ctx);

    abstract record BoolNode;
    sealed record BConst(bool Value) : BoolNode;
    sealed record BCmp(Operand Left, string Op, Operand Right) : BoolNode;
    sealed record BAnd(BoolNode A, BoolNode B) : BoolNode;
    sealed record BOr(BoolNode A, BoolNode B) : BoolNode;
    sealed record BNot(BoolNode A) : BoolNode;

    // ---------- Zustand ----------

    readonly Dictionary<string, MethodDeclarationSyntax> _methods = new();
    readonly Dictionary<string, string> _fields = new();
    readonly Dictionary<string, ushort> _consts = new();
    readonly List<(string Reg, ushort Value)> _init = new();
    readonly Dictionary<string, List<Line>> _scripts = new();
    readonly Queue<(string Name, List<Item> Items, string? Cont)> _queue = new();
    readonly HashSet<string> _oidNames = new();
    readonly HashSet<string> _helpers = new();
    string _entry = "";
    int _blockCounter;
    int _temp;

    public static CompileResult Compile(string source) => new ProgramCompiler().Run(source);

    CompileResult Run(string source)
    {
        var result = new CompileResult();
        var tree = CSharpSyntaxTree.ParseText(source);
        foreach (var d in tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error))
        {
            var pos = d.Location.GetLineSpan().StartLinePosition;
            result.Errors.Add(new CompileError(pos.Line + 1, pos.Character + 1, d.GetMessage(CultureInfo.GetCultureInfo("de-DE"))));
        }
        if (!result.Success)
            return result;

        var root = tree.GetCompilationUnitRoot();
        var oidMethods = new List<(string Name, MethodDeclarationSyntax Method)>();

        void Guarded(Action action)
        {
            try
            {
                action();
            }
            catch (CompileException ex)
            {
                result.Errors.Add(ErrorAt(ex.Node, ex.Message));
            }
        }

        // Platzhalter für die IDE (class TipToi, class OidAttribute) werden übersprungen
        foreach (var cls in root.DescendantNodes().OfType<ClassDeclarationSyntax>()
                     .Where(c => c.Identifier.Text is not ("TipToi" or "OidAttribute")))
        {
            foreach (var member in cls.Members)
                Guarded(() => CollectMember(member, oidMethods));
        }
        if (!result.Success)
            return result;
        if (oidMethods.Count == 0)
        {
            result.Errors.Add(new CompileError(0, 0, "Keine Methode mit [Oid] gefunden – ohne sie gibt es nichts zum Antippen."));
            return result;
        }

        foreach (var (name, method) in oidMethods)
        {
            Guarded(() =>
            {
                _entry = name;
                _blockCounter = 0;
                _queue.Clear();
                var ctx = new MethodCtx { Method = method, Prefix = method.Identifier.Text, CallChain = new[] { method.Identifier.Text } };
                _queue.Enqueue((name, BodyItems(method, ctx), null));
                while (_queue.Count > 0)
                {
                    var (blockName, items, cont) = _queue.Dequeue();
                    var lines = Seq(items, new List<Cond>(), new List<Cmd>(), cont);
                    AddScript(blockName, lines);
                }
            });
        }
        if (!result.Success)
            return result;

        MergeDuplicates();

        var init = _init.Where(i => i.Value != 0).Select(i => $"${i.Reg} := {i.Value}").ToList();
        result.Init = string.Join(" ", init);
        // [Oid]-Skripte in Reihenfolge des Quelltexts, danach die Hilfsskripte
        var order = oidMethods.Select(m => m.Name).ToList();
        int HelperNumber(string n) => int.TryParse(n[(n.LastIndexOf('_') + 1)..], out var k) ? k : 0;
        order.AddRange(_scripts.Keys.Where(n => !_oidNames.Contains(n))
            .OrderBy(n => order.IndexOf(n[..n.LastIndexOf('_')])).ThenBy(HelperNumber));
        result.EntryPoints.AddRange(oidMethods.Select(m => m.Name));
        foreach (var name in order)
            result.Scripts.Add(new CompiledScript(name, _scripts[name].Select(FormatLine).ToList()));
        return result;
    }

    static CompileError ErrorAt(SyntaxNode? node, string message)
    {
        if (node == null || node.SyntaxTree == null)
            return new CompileError(0, 0, message);
        var pos = node.GetLocation().GetLineSpan().StartLinePosition;
        return new CompileError(pos.Line + 1, pos.Character + 1, message);
    }

    static string FormatLine(Line l) =>
        string.Join(" ", l.Conds.Select(c => c.ToString()).Concat(l.Cmds.Select(c => c.Text)));

    // ---------- Deklarationen ----------

    void CollectMember(MemberDeclarationSyntax member, List<(string, MethodDeclarationSyntax)> oidMethods)
    {
        switch (member)
        {
            case FieldDeclarationSyntax field:
                bool isConst = field.Modifiers.Any(SyntaxKind.ConstKeyword);
                CheckType(field.Declaration.Type);
                foreach (var v in field.Declaration.Variables)
                {
                    var name = v.Identifier.Text;
                    if (_fields.ContainsKey(name) || _consts.ContainsKey(name))
                        throw new CompileException(v, $"„{name}“ ist doppelt deklariert.");
                    ushort value = 0;
                    if (v.Initializer != null)
                        value = ConstEval(v.Initializer.Value, null)
                                ?? throw new CompileException(v.Initializer.Value, "Startwerte müssen konstant sein.");
                    if (isConst)
                        _consts[name] = value;
                    else
                    {
                        _fields[name] = name;
                        _init.Add((name, value));
                    }
                }
                break;

            case MethodDeclarationSyntax method:
                var mname = method.Identifier.Text;
                if (_methods.ContainsKey(mname))
                    throw new CompileException(method, $"Methode „{mname}“ ist doppelt vorhanden (Überladungen sind nicht möglich).");
                if (method.ReturnType is not PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.VoidKeyword })
                    throw new CompileException(method.ReturnType, "Methoden müssen void sein – der Stift kennt keine Rückgabewerte.");
                foreach (var p in method.ParameterList.Parameters)
                    CheckType(p.Type);
                _methods[mname] = method;
                var oid = method.AttributeLists.SelectMany(a => a.Attributes)
                    .FirstOrDefault(a => a.Name.ToString() is "Oid" or "OidAttribute");
                if (oid != null)
                {
                    if (method.ParameterList.Parameters.Count > 0)
                        throw new CompileException(method, "[Oid]-Methoden dürfen keine Parameter haben.");
                    var scriptName = mname;
                    if (oid.ArgumentList?.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax lit && lit.Token.Value is string s)
                        scriptName = s;
                    if (!Regex.IsMatch(scriptName, @"^[\p{L}\p{N}_]+$"))
                        throw new CompileException(oid, $"„{scriptName}“ ist kein gültiger Skriptname (nur Buchstaben, Ziffern, _).");
                    if (!_oidNames.Add(scriptName))
                        throw new CompileException(oid, $"Der Skriptname „{scriptName}“ ist doppelt vergeben.");
                    oidMethods.Add((scriptName, method));
                }
                break;

            case ClassDeclarationSyntax:
                break; // verschachtelte Klassen werden separat durchlaufen

            default:
                throw new CompileException(member, "Nur Felder, Konstanten und Methoden werden unterstützt.");
        }
    }

    static void CheckType(TypeSyntax? type)
    {
        if (type is PredefinedTypeSyntax p && p.Keyword.Kind() is SyntaxKind.BoolKeyword or SyntaxKind.ByteKeyword
                or SyntaxKind.SByteKeyword or SyntaxKind.ShortKeyword or SyntaxKind.UShortKeyword or SyntaxKind.IntKeyword
                or SyntaxKind.UIntKeyword or SyntaxKind.LongKeyword or SyntaxKind.ULongKeyword)
            return;
        if (type is IdentifierNameSyntax { Identifier.Text: "var" })
            return;
        throw new CompileException(type, $"Der Typ „{type}“ wird nicht unterstützt – erlaubt sind ganze Zahlen (0 bis 65535) und bool.");
    }

    // ---------- Namensauflösung ----------

    static List<Item> BodyItems(MethodDeclarationSyntax method, MethodCtx ctx)
    {
        if (method.Body != null)
            return method.Body.Statements.Select(s => (Item)new StmtItem(s, ctx)).ToList();
        if (method.ExpressionBody != null)
            return new List<Item> { new StmtItem(SyntaxFactory.ExpressionStatement(method.ExpressionBody.Expression), ctx) };
        return new List<Item>();
    }

    static string NameOf(ExpressionSyntax e) => e switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax or IdentifierNameSyntax } m => m.Name.Identifier.Text,
        _ => "",
    };

    string? RegisterOf(ExpressionSyntax e, MethodCtx? ctx)
    {
        e = Unwrap(e);
        var name = NameOf(e);
        if (name == "")
            return null;
        if (e is IdentifierNameSyntax && ctx != null && ctx.Locals.TryGetValue(name, out var local))
            return local;
        return _fields.TryGetValue(name, out var field) ? field : null;
    }

    static ExpressionSyntax Unwrap(ExpressionSyntax e)
    {
        while (true)
        {
            switch (e)
            {
                case ParenthesizedExpressionSyntax p: e = p.Expression; break;
                case CastExpressionSyntax c: e = c.Expression; break;
                case CheckedExpressionSyntax ch: e = ch.Expression; break;
                default: return e;
            }
        }
    }

    ushort? ConstEval(ExpressionSyntax e, MethodCtx? ctx)
    {
        e = Unwrap(e);
        switch (e)
        {
            case LiteralExpressionSyntax lit:
                return lit.Token.Value switch
                {
                    bool b => (ushort)(b ? 1 : 0),
                    int i when i is >= 0 and <= ushort.MaxValue => (ushort)i,
                    uint u when u <= ushort.MaxValue => (ushort)u,
                    long lv when lv is >= 0 and <= ushort.MaxValue => (ushort)lv,
                    ulong ul when ul <= ushort.MaxValue => (ushort)ul,
                    char ch => ch,
                    int or uint or long or ulong => throw new CompileException(e, "Zahlen müssen zwischen 0 und 65535 liegen."),
                    _ => throw new CompileException(e, "Nur ganze Zahlen und true/false sind als Werte erlaubt."),
                };
            case IdentifierNameSyntax or MemberAccessExpressionSyntax:
                var name = NameOf(e);
                if (ctx != null && e is IdentifierNameSyntax && ctx.LocalConsts.TryGetValue(name, out var lc))
                    return lc;
                if (RegisterOf(e, ctx) != null)
                    return null;
                return _consts.TryGetValue(name, out var c2) ? c2 : null;
            case PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.UnaryMinusExpression } neg when ConstEval(neg.Operand, ctx) is { } nv:
                return unchecked((ushort)-nv);
            case BinaryExpressionSyntax bin when ArithOp(bin.Kind()) != null:
                var l = ConstEval(bin.Left, ctx);
                var r = ConstEval(bin.Right, ctx);
                if (l == null || r == null)
                    return null;
                return Fold(bin, bin.Kind(), l.Value, r.Value);
            default:
                return null;
        }
    }

    static ushort Fold(SyntaxNode node, SyntaxKind kind, ushort a, ushort b)
    {
        if (kind is SyntaxKind.DivideExpression or SyntaxKind.ModuloExpression && b == 0)
            throw new CompileException(node, "Division durch 0.");
        return unchecked(kind switch
        {
            SyntaxKind.AddExpression => (ushort)(a + b),
            SyntaxKind.SubtractExpression => (ushort)(a - b),
            SyntaxKind.MultiplyExpression => (ushort)(a * b),
            SyntaxKind.DivideExpression => (ushort)(a / b),
            SyntaxKind.ModuloExpression => (ushort)(a % b),
            SyntaxKind.BitwiseAndExpression => (ushort)(a & b),
            SyntaxKind.BitwiseOrExpression => (ushort)(a | b),
            SyntaxKind.ExclusiveOrExpression => (ushort)(a ^ b),
            SyntaxKind.LeftShiftExpression => (ushort)(a << b),
            SyntaxKind.RightShiftExpression => (ushort)(a >> b),
            _ => throw new CompileException(node, "Unbekannter Operator."),
        });
    }

    static string? ArithOp(SyntaxKind kind) => kind switch
    {
        SyntaxKind.AddExpression or SyntaxKind.AddAssignmentExpression => "+=",
        SyntaxKind.SubtractExpression or SyntaxKind.SubtractAssignmentExpression => "-=",
        SyntaxKind.MultiplyExpression or SyntaxKind.MultiplyAssignmentExpression => "*=",
        SyntaxKind.DivideExpression or SyntaxKind.DivideAssignmentExpression => "/=",
        SyntaxKind.ModuloExpression or SyntaxKind.ModuloAssignmentExpression => "%=",
        SyntaxKind.BitwiseAndExpression or SyntaxKind.AndAssignmentExpression => "&=",
        SyntaxKind.BitwiseOrExpression or SyntaxKind.OrAssignmentExpression => "|=",
        SyntaxKind.ExclusiveOrExpression or SyntaxKind.ExclusiveOrAssignmentExpression => "^=",
        SyntaxKind.LeftShiftExpression or SyntaxKind.LeftShiftAssignmentExpression => "<<",
        SyntaxKind.RightShiftExpression or SyntaxKind.RightShiftAssignmentExpression => ">>",
        _ => null,
    };

    // ---------- Ausdrücke ----------

    string NewTemp() => $"_t{++_temp}";

    /// <summary>Liefert einen Operanden; zusammengesetzte Ausdrücke werden in ein Hilfsregister berechnet.</summary>
    Operand Value(ExpressionSyntax e, MethodCtx ctx, List<Cmd> cmds)
    {
        if (ConstEval(e, ctx) is { } c)
            return Operand.Const(c);
        if (RegisterOf(e, ctx) is { } reg)
            return Operand.Register(reg);
        var t = NewTemp();
        Gen(e, t, ctx, cmds);
        return Operand.Register(t);
    }

    /// <summary>Berechnet <paramref name="e"/> nach Register <paramref name="dest"/>; e darf dest nicht lesen.</summary>
    void Gen(ExpressionSyntax e, string dest, MethodCtx ctx, List<Cmd> cmds)
    {
        if (ConstEval(e, ctx) is { } c)
        {
            cmds.Add(Set(dest, Operand.Const(c)));
            return;
        }
        if (RegisterOf(e, ctx) is { } reg)
        {
            cmds.Add(Set(dest, Operand.Register(reg)));
            return;
        }
        e = Unwrap(e);
        switch (e)
        {
            case BinaryExpressionSyntax bin when ArithOp(bin.Kind()) is { } op:
                Gen(bin.Left, dest, ctx, cmds);
                AppendOp(bin, dest, op, bin.Right, ctx, cmds);
                return;
            case PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.UnaryMinusExpression } neg:
                var v = Value(neg.Operand, ctx, cmds);
                cmds.Add(Set(dest, Operand.Const(0)));
                cmds.Add(new Cmd($"${dest} -= {v}", dest));
                return;
            case InvocationExpressionSyntax inv when NameOf(inv.Expression) is "Random" or "Timer":
                var args = inv.ArgumentList.Arguments;
                if (args.Count != 1)
                    throw new CompileException(inv, $"{NameOf(inv.Expression)}(n) erwartet genau eine Zahl.");
                var n = ConstEval(args[0].Expression, ctx)
                        ?? throw new CompileException(args[0], "Die Obergrenze muss eine Konstante sein.");
                if (NameOf(inv.Expression) == "Timer")
                {
                    cmds.Add(new Cmd($"T(${dest},{n})", dest));
                    return;
                }
                if (n == 0)
                    throw new CompileException(args[0], "Random(0) ist nicht möglich.");
                // Pseudo-Zufallszahl: Zähler des Stifts als Startwert, dann linearer Kongruenzgenerator
                cmds.Add(new Cmd("T($_zufall_t,65535)", "_zufall_t"));
                cmds.Add(new Cmd("$_zufall += $_zufall_t", "_zufall"));
                cmds.Add(new Cmd("$_zufall *= 25173", "_zufall"));
                cmds.Add(new Cmd("$_zufall += 13849", "_zufall"));
                cmds.Add(Set(dest, Operand.Register("_zufall")));
                cmds.Add(new Cmd($"${dest} %= {n}", dest));
                return;
            case BinaryExpressionSyntax or PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalNotExpression }:
                throw new CompileException(e, "Vergleiche und logische Ausdrücke sind nur in Bedingungen (if) möglich.");
            case InvocationExpressionSyntax:
                throw new CompileException(e, "Methoden haben keine Rückgabewerte; nur Random(n) und Timer(n) liefern Zahlen.");
            case IdentifierNameSyntax or MemberAccessExpressionSyntax:
                throw new CompileException(e, $"„{e}“ ist unbekannt.");
            default:
                throw new CompileException(e, $"Dieser Ausdruck wird nicht unterstützt: {e}");
        }
    }

    void AppendOp(SyntaxNode node, string dest, string op, ExpressionSyntax right, MethodCtx ctx, List<Cmd> cmds)
    {
        if (op is "<<" or ">>")
        {
            var k = ConstEval(right, ctx) ?? throw new CompileException(right, "Verschiebungen sind nur um konstante Werte möglich.");
            if (k > 15)
                throw new CompileException(right, "Verschiebung um mehr als 15 Bit ist nicht sinnvoll.");
            cmds.Add(new Cmd($"${dest} {(op == "<<" ? "*=" : "/=")} {1 << k}", dest));
            return;
        }
        var v = Value(right, ctx, cmds);
        if (v.IsConst && v.Value == 0 && op is "/=" or "%=")
            throw new CompileException(node, "Division durch 0.");
        cmds.Add(new Cmd($"${dest} {op} {v}", dest));
    }

    static Cmd Set(string dest, Operand v) => new($"${dest} := {v}", dest);

    bool Reads(ExpressionSyntax e, string reg, MethodCtx ctx) =>
        e.DescendantNodesAndSelf().OfType<ExpressionSyntax>()
            .Any(x => x is IdentifierNameSyntax or MemberAccessExpressionSyntax && RegisterOf(x, ctx) == reg);

    // ---------- Bedingungen ----------

    BoolNode Bool(ExpressionSyntax e, MethodCtx ctx, List<Cmd> prep)
    {
        switch (e)
        {
            case ParenthesizedExpressionSyntax p:
                return Bool(p.Expression, ctx, prep);
            case LiteralExpressionSyntax { RawKind: (int)SyntaxKind.TrueLiteralExpression }:
                return new BConst(true);
            case LiteralExpressionSyntax { RawKind: (int)SyntaxKind.FalseLiteralExpression }:
                return new BConst(false);
            case PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalNotExpression } not:
                return new BNot(Bool(not.Operand, ctx, prep));
            case BinaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalAndExpression } and:
                return new BAnd(Bool(and.Left, ctx, prep), Bool(and.Right, ctx, prep));
            case BinaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalOrExpression } or:
                return new BOr(Bool(or.Left, ctx, prep), Bool(or.Right, ctx, prep));
            case BinaryExpressionSyntax bin when CmpOp(bin.Kind()) is { } op:
                return new BCmp(Value(bin.Left, ctx, prep), op, Value(bin.Right, ctx, prep));
            default:
                // bool-Register oder Zahl: wahr, wenn ungleich 0
                return new BCmp(Value(e, ctx, prep), "!=", Operand.Const(0));
        }
    }

    static string? CmpOp(SyntaxKind kind) => kind switch
    {
        SyntaxKind.EqualsExpression => "==",
        SyntaxKind.NotEqualsExpression => "!=",
        SyntaxKind.LessThanExpression => "<",
        SyntaxKind.GreaterThanExpression => ">",
        SyntaxKind.LessThanOrEqualExpression => "<=",
        SyntaxKind.GreaterThanOrEqualExpression => ">=",
        _ => null,
    };

    static string Negate(string op) => op switch
    {
        "==" => "!=",
        "!=" => "==",
        "<" => ">=",
        ">=" => "<",
        ">" => "<=",
        _ => ">",
    };

    static bool Compare(ushort a, string op, ushort b) => op switch
    {
        "==" => a == b,
        "!=" => a != b,
        "<" => a < b,
        ">" => a > b,
        "<=" => a <= b,
        _ => a >= b,
    };

    /// <summary>Disjunktive Normalform: Liste von UND-Verknüpfungen. Leere Liste = falsch, [[]] = wahr.</summary>
    static List<List<Cond>> Dnf(BoolNode n, bool negate, SyntaxNode node)
    {
        List<List<Cond>> True() => new() { new List<Cond>() };
        List<List<Cond>> False() => new();
        List<List<Cond>> result;
        switch (n)
        {
            case BConst c:
                result = c.Value != negate ? True() : False();
                break;
            case BCmp cmp:
                var op = negate ? Negate(cmp.Op) : cmp.Op;
                if (cmp.Left.IsConst && cmp.Right.IsConst)
                    result = Compare(cmp.Left.Value, op, cmp.Right.Value) ? True() : False();
                else
                    result = new List<List<Cond>> { new() { new Cond(cmp.Left, op, cmp.Right) } };
                break;
            case BNot not:
                result = Dnf(not.A, !negate, node);
                break;
            case BAnd when !negate:
            case BOr when negate:
                var (a, b) = n is BAnd x ? (x.A, x.B) : (((BOr)n).A, ((BOr)n).B);
                var left = Dnf(a, negate, node);
                var right = Dnf(b, negate, node);
                result = left.SelectMany(l => right.Select(r => l.Concat(r).Distinct().ToList())).ToList();
                break;
            default:
                var (a2, b2) = n is BAnd y ? (y.A, y.B) : (((BOr)n).A, ((BOr)n).B);
                result = Dnf(a2, negate, node).Concat(Dnf(b2, negate, node)).ToList();
                break;
        }
        if (result.Count > MaxTermsPerCondition)
            throw new CompileException(node, "Die Bedingung ist zu komplex (zu viele Fälle). Bitte vereinfachen oder aufteilen.");
        return result;
    }

    // ---------- Anweisungen ----------

    string? NewBlock(List<Item> items, string? cont)
    {
        if (items.Count == 0)
            return cont;
        var name = $"{_entry}_{++_blockCounter}";
        _helpers.Add(name);
        _queue.Enqueue((name, items, cont));
        return name;
    }

    static Line Finish(List<Cond> guard, List<Cmd> cmds, string? jump)
    {
        var all = new List<Cmd>(cmds);
        if (jump != null)
            all.Add(new Cmd($"J({jump})"));
        return new Line { Conds = new List<Cond>(guard), Cmds = all };
    }

    /// <summary>
    /// Übersetzt eine Anweisungsfolge in Zeilen eines Skripts. <paramref name="guard"/> sind die Bedingungen,
    /// unter denen die Folge läuft, <paramref name="pre"/> bereits gesammelte Befehle und
    /// <paramref name="cont"/> das Skript, das danach folgt (null = Ende).
    /// </summary>
    List<Line> Seq(List<Item> items, List<Cond> guard, List<Cmd> pre, string? cont)
    {
        var cmds = new List<Cmd>(pre);
        var queue = new List<Item>(items);
        for (int i = 0; i < queue.Count; i++)
        {
            var item = queue[i];
            var rest = queue.Skip(i + 1).ToList();
            var ctx = item.Ctx;

            if (item is PreparedIf prepared)
                return Branches(prepared.Branches, prepared.Else, guard, cmds, rest, cont, ctx);

            var stmt = ((StmtItem)item).Stmt;
            _temp = 0;
            switch (stmt)
            {
                case BlockSyntax block:
                    queue.InsertRange(i + 1, block.Statements.Select(s => (Item)new StmtItem(s, ctx)));
                    break;

                case EmptyStatementSyntax:
                    break;

                case LocalDeclarationStatementSyntax decl:
                    // bool b = a > 3;  →  Variable anlegen, Zuweisung als eigene Anweisung
                    var deferred = Declare(decl, ctx, cmds);
                    queue.InsertRange(i + 1, deferred.Select(d => (Item)new StmtItem(d, ctx)));
                    break;

                case ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SimpleAssignmentExpression } a }
                    when IsCondition(a.Right):
                    // x = a > 3;  →  if (a > 3) x = 1; else x = 0;
                    var asIf = SyntaxFactory.IfStatement(a.Right,
                        SyntaxFactory.ExpressionStatement(a.WithRight(SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression))),
                        SyntaxFactory.ElseClause(SyntaxFactory.ExpressionStatement(
                            a.WithRight(SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression)))));
                    return If(asIf, ctx, guard, cmds, rest, cont);

                case ExpressionStatementSyntax { Expression: InvocationExpressionSyntax inv } when IsUserMethod(inv):
                    return Call(inv, ctx, guard, cmds, rest, cont);

                case ExpressionStatementSyntax es:
                    Statement(es.Expression, ctx, cmds);
                    break;

                case ReturnStatementSyntax ret:
                    if (ret.Expression != null)
                        throw new CompileException(ret, "return mit Wert ist nicht möglich – der Stift kennt keine Rückgabewerte.");
                    return new List<Line> { Finish(guard, cmds, ctx.Ret) };

                case IfStatementSyntax ifs:
                    return If(ifs, ctx, guard, cmds, rest, cont);

                case SwitchStatementSyntax sw:
                    return Switch(sw, ctx, guard, cmds, rest, cont);

                case ForStatementSyntax forStmt:
                    queue.InsertRange(i + 1, UnrollFor(forStmt, ctx));
                    break;

                case ForEachStatementSyntax forEach:
                    queue.InsertRange(i + 1, UnrollForEach(forEach, ctx));
                    break;

                case WhileStatementSyntax or DoStatementSyntax:
                    throw new CompileException(stmt,
                        "while- und do-Schleifen sind auf dem Stift nicht möglich (ein Skript läuft nur einmal pro Antippen). " +
                        "Möglich sind for-Schleifen mit fester Anzahl; Wiederholungen sonst durch erneutes Antippen.");

                case BreakStatementSyntax or ContinueStatementSyntax:
                    throw new CompileException(stmt, stmt is BreakStatementSyntax
                        ? "break ist nur am Ende eines case-Abschnitts erlaubt (nicht in Schleifen)."
                        : "continue ist nicht möglich – stattdessen den Rest des Schleifenrumpfs in ein if setzen.");

                default:
                    throw new CompileException(stmt, $"Diese Anweisung wird nicht unterstützt: {stmt.Kind()}");
            }
        }
        return new List<Line> { Finish(guard, cmds, cont) };
    }

    /// <summary>Wird die Variable irgendwo in der Methode nach der Deklaration verändert?</summary>
    static bool IsAssigned(MethodDeclarationSyntax method, string name) =>
        method.DescendantNodes().Any(n => n switch
        {
            AssignmentExpressionSyntax a => NameOf(Unwrap(a.Left)) == name,
            PostfixUnaryExpressionSyntax p => NameOf(Unwrap(p.Operand)) == name,
            PrefixUnaryExpressionSyntax p when p.Kind() is SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression
                => NameOf(Unwrap(p.Operand)) == name,
            _ => false,
        });

    static bool IsCondition(ExpressionSyntax e) => Unwrap(e).Kind() is SyntaxKind.LogicalAndExpression
        or SyntaxKind.LogicalOrExpression or SyntaxKind.LogicalNotExpression or SyntaxKind.EqualsExpression
        or SyntaxKind.NotEqualsExpression or SyntaxKind.LessThanExpression or SyntaxKind.GreaterThanExpression
        or SyntaxKind.LessThanOrEqualExpression or SyntaxKind.GreaterThanOrEqualExpression;

    /// <summary>Legt lokale Variablen an; Zuweisungen von Bedingungen werden als Anweisungen zurückgegeben.</summary>
    List<StatementSyntax> Declare(LocalDeclarationStatementSyntax decl, MethodCtx ctx, List<Cmd> cmds)
    {
        var deferred = new List<StatementSyntax>();
        CheckType(decl.Declaration.Type);
        foreach (var v in decl.Declaration.Variables)
        {
            var name = v.Identifier.Text;
            if (decl.IsConst)
            {
                ctx.LocalConsts[name] = v.Initializer != null ? ConstEval(v.Initializer.Value, ctx)
                    ?? throw new CompileException(v, "Konstanten brauchen einen konstanten Wert.") : (ushort)0;
                continue;
            }
            // Nie geänderte Variable mit konstantem Wert (z. B. int q = i * i in einer ausgerollten Schleife):
            // als Konstante behandeln, spart Register und Befehle
            if (v.Initializer != null && !IsAssigned(ctx.Method, name) && ConstEval(v.Initializer.Value, ctx) is { } constant)
            {
                ctx.Locals.Remove(name);
                ctx.LocalConsts[name] = constant;
                continue;
            }
            ctx.LocalConsts.Remove(name);
            var reg = $"{ctx.Prefix}_{name}";
            ctx.Locals[name] = reg;
            if (v.Initializer != null && IsCondition(v.Initializer.Value))
                deferred.Add(SyntaxFactory.ExpressionStatement(SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression, SyntaxFactory.IdentifierName(name), v.Initializer.Value)));
            else if (v.Initializer != null)
                Assign(reg, v.Initializer.Value, ctx, cmds);
            else
                cmds.Add(Set(reg, Operand.Const(0)));
        }
        return deferred;
    }

    void Assign(string target, ExpressionSyntax value, MethodCtx ctx, List<Cmd> cmds)
    {
        var u = Unwrap(value);
        // x = x op y  →  x op= y
        if (u is BinaryExpressionSyntax bin && ArithOp(bin.Kind()) is { } op && RegisterOf(bin.Left, ctx) == target
            && !Reads(bin.Right, target, ctx))
        {
            AppendOp(bin, target, op, bin.Right, ctx, cmds);
            return;
        }
        if (Reads(value, target, ctx))
        {
            var t = NewTemp();
            Gen(value, t, ctx, cmds);
            cmds.Add(Set(target, Operand.Register(t)));
            return;
        }
        Gen(value, target, ctx, cmds);
    }

    string Target(ExpressionSyntax e, MethodCtx ctx)
    {
        if (RegisterOf(e, ctx) is { } reg)
            return reg;
        var name = NameOf(Unwrap(e));
        if (ctx.LoopVars.Contains(name))
            throw new CompileException(e, $"Die Schleifenvariable „{name}“ darf im Schleifenrumpf nicht geändert werden.");
        if (_consts.ContainsKey(name) || ctx.LocalConsts.ContainsKey(name))
            throw new CompileException(e, $"„{name}“ ist eine Konstante und kann nicht geändert werden.");
        throw new CompileException(e, $"„{e}“ ist kein Feld und keine Variable.");
    }

    void Statement(ExpressionSyntax e, MethodCtx ctx, List<Cmd> cmds)
    {
        switch (e)
        {
            case AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SimpleAssignmentExpression } a:
                Assign(Target(a.Left, ctx), a.Right, ctx, cmds);
                break;
            case AssignmentExpressionSyntax a when ArithOp(a.Kind()) is { } op:
                AppendOp(a, Target(a.Left, ctx), op, a.Right, ctx, cmds);
                break;
            case PostfixUnaryExpressionSyntax or PrefixUnaryExpressionSyntax
                when e.Kind() is SyntaxKind.PostIncrementExpression or SyntaxKind.PreIncrementExpression
                    or SyntaxKind.PostDecrementExpression or SyntaxKind.PreDecrementExpression:
                var operand = e is PostfixUnaryExpressionSyntax post ? post.Operand : ((PrefixUnaryExpressionSyntax)e).Operand;
                var reg = Target(operand, ctx);
                bool inc = e.Kind() is SyntaxKind.PostIncrementExpression or SyntaxKind.PreIncrementExpression;
                cmds.Add(new Cmd($"${reg} {(inc ? "+=" : "-=")} 1", reg));
                break;
            case InvocationExpressionSyntax inv:
                Builtin(inv, ctx, cmds);
                break;
            default:
                throw new CompileException(e, $"Diese Anweisung wird nicht unterstützt: {e}");
        }
    }

    static string Media(ArgumentSyntax a)
    {
        if (a.Expression is not LiteralExpressionSyntax { Token.Value: string s } || s.Length == 0)
            throw new CompileException(a, "Erwartet wird der Name eines Textes oder einer Audiodatei in Anführungszeichen.");
        if (s.Contains('"'))
            throw new CompileException(a, "Audio-Namen dürfen keine Anführungszeichen enthalten.");
        return Regex.IsMatch(s, @"^[\p{L}\p{N}_.]+$") ? s : $"\"{s}\"";
    }

    void Builtin(InvocationExpressionSyntax inv, MethodCtx ctx, List<Cmd> cmds)
    {
        var name = NameOf(inv.Expression);
        var args = inv.ArgumentList.Arguments;
        switch (name)
        {
            case "Play" or "PlayAll" or "PlayRandom":
                if (args.Count == 0)
                    throw new CompileException(inv, $"{name} braucht mindestens einen Namen.");
                var media = string.Join(",", args.Select(Media));
                if (name == "PlayRandom" && args.Count > 1)
                    cmds.Add(new Cmd($"P({media})"));
                else if (args.Count == 1)
                    cmds.Add(new Cmd($"P({media})"));
                else
                    cmds.Add(new Cmd($"PA({media})"));
                break;
            case "Cancel":
                cmds.Add(new Cmd("C"));
                break;
            case "Random" or "Timer":
                throw new CompileException(inv, $"Das Ergebnis von {name}(…) muss einer Variablen zugewiesen werden.");
            default:
                throw new CompileException(inv, $"Unbekannte Methode „{name}“. Verfügbar: Play, PlayAll, PlayRandom, Cancel, Random, Timer und eigene Methoden.");
        }
    }

    bool IsUserMethod(InvocationExpressionSyntax inv) => inv.Expression is IdentifierNameSyntax or MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax }
        && _methods.ContainsKey(NameOf(inv.Expression));

    List<Line> Call(InvocationExpressionSyntax inv, MethodCtx ctx, List<Cond> guard, List<Cmd> cmds, List<Item> rest, string? cont)
    {
        var name = NameOf(inv.Expression);
        var method = _methods[name];
        if (ctx.CallChain.Contains(name))
            throw new CompileException(inv, $"Rekursion ({string.Join(" → ", ctx.CallChain.Append(name))}) ist auf dem Stift nicht möglich.");

        var parameters = method.ParameterList.Parameters;
        var args = inv.ArgumentList.Arguments;
        if (parameters.Count != args.Count)
            throw new CompileException(inv, $"{name} erwartet {parameters.Count} Argument(e).");

        // Argumente zuerst in Hilfsregister, damit sie sich nicht gegenseitig beeinflussen
        var prefix = string.Join("_", ctx.CallChain.Skip(1).Append(name));
        var values = args.Select(a => Value(a.Expression, ctx, cmds)).ToList();
        bool hasReturn = method.DescendantNodes().OfType<ReturnStatementSyntax>().Any();
        var after = hasReturn ? NewBlock(rest, cont) : null;
        var callee = new MethodCtx
        {
            Method = method,
            Prefix = prefix,
            Ret = hasReturn ? after : null,
            CallChain = ctx.CallChain.Append(name).ToList(),
        };
        for (int i = 0; i < parameters.Count; i++)
        {
            var pname = parameters[i].Identifier.Text;
            // konstantes Argument und Parameter wird nicht verändert → Konstante statt Register
            if (values[i].IsConst && !IsAssigned(method, pname))
            {
                callee.LocalConsts[pname] = values[i].Value;
                continue;
            }
            var reg = $"{prefix}_{pname}";
            callee.Locals[pname] = reg;
            cmds.Add(Set(reg, values[i]));
        }

        var body = BodyItems(method, callee);
        return hasReturn
            ? Seq(body, guard, cmds, after)
            : Seq(body.Concat(rest).ToList(), guard, cmds, cont);
    }

    List<Line> If(IfStatementSyntax ifs, MethodCtx ctx, List<Cond> guard, List<Cmd> cmds, List<Item> rest, string? cont)
    {
        var prep = new List<Cmd>();
        var branches = new List<(List<List<Cond>>, List<Item>)>();
        List<Item>? elseBody = null;
        for (StatementSyntax? s = ifs; s != null;)
        {
            if (s is IfStatementSyntax current)
            {
                var dnf = Dnf(Bool(current.Condition, ctx, prep), false, current.Condition);
                branches.Add((dnf, new List<Item> { new StmtItem(current.Statement, ctx) }));
                s = current.Else?.Statement;
            }
            else
            {
                elseBody = new List<Item> { new StmtItem(s, ctx) };
                s = null;
            }
        }
        return Conditional(branches, elseBody, prep, ctx, guard, cmds, rest, cont);
    }

    List<Line> Switch(SwitchStatementSyntax sw, MethodCtx ctx, List<Cond> guard, List<Cmd> cmds, List<Item> rest, string? cont)
    {
        var prep = new List<Cmd>();
        var subject = Value(sw.Expression, ctx, prep);
        var branches = new List<(List<List<Cond>>, List<Item>)>();
        List<Item>? elseBody = null;
        foreach (var section in sw.Sections)
        {
            var statements = section.Statements.ToList();
            if (statements.Count == 0 || statements[^1] is not (BreakStatementSyntax or ReturnStatementSyntax))
                throw new CompileException(section, "Jeder case-Abschnitt muss mit break oder return enden.");
            if (statements[^1] is BreakStatementSyntax)
                statements.RemoveAt(statements.Count - 1);
            var body = statements.Select(st => (Item)new StmtItem(st, ctx)).ToList();

            var terms = new List<List<Cond>>();
            foreach (var label in section.Labels)
            {
                switch (label)
                {
                    case DefaultSwitchLabelSyntax:
                        elseBody = body;
                        break;
                    case CaseSwitchLabelSyntax c:
                        var v = ConstEval(c.Value, ctx) ?? throw new CompileException(c.Value, "case-Werte müssen konstant sein.");
                        terms.AddRange(Dnf(new BCmp(subject, "==", Operand.Const(v)), false, c));
                        break;
                    default:
                        throw new CompileException(label, "Nur einfache case-Werte werden unterstützt.");
                }
            }
            if (terms.Count > 0 && elseBody != body)
                branches.Add((terms, body));
        }
        return Conditional(branches, elseBody, prep, ctx, guard, cmds, rest, cont);
    }

    List<Line> Conditional(List<(List<List<Cond>> Dnf, List<Item> Body)> branches, List<Item>? elseBody, List<Cmd> prep,
        MethodCtx ctx, List<Cond> guard, List<Cmd> cmds, List<Item> rest, string? cont)
    {
        // Stehen alle Bedingungen schon beim Übersetzen fest (z. B. i == 2 in einer ausgerollten Schleife),
        // wird der passende Zweig direkt eingesetzt – ohne Bedingung und ohne Sprung.
        bool IsConstant(List<List<Cond>> dnf) => dnf.Count == 0 || dnf.Count == 1 && dnf[0].Count == 0;
        if (prep.Count == 0 && branches.All(b => IsConstant(b.Dnf)))
        {
            var chosen = branches.FirstOrDefault(b => b.Dnf.Count == 1).Body ?? elseBody ?? new List<Item>();
            return Seq(chosen.Concat(rest).ToList(), guard, cmds, cont);
        }

        // Bedingungen werden vor den Befehlen einer Zeile geprüft. Ändern vorangehende Befehle Register,
        // die in den Bedingungen vorkommen (oder müssen Werte erst berechnet werden), geht es in einem
        // Hilfsskript weiter.
        var read = branches.SelectMany(b => b.Dnf.SelectMany(t => t.SelectMany(c => new[] { c.Left.Reg, c.Right.Reg })))
            .Where(r => r != null).ToHashSet();
        if (prep.Count > 0 || cmds.Any(c => c.Writes != null && read.Contains(c.Writes)))
        {
            var block = NewBlock(new List<Item> { new PreparedIf(branches, elseBody, ctx) }.Concat(rest).ToList(), cont);
            return new List<Line> { Finish(guard, cmds.Concat(prep).ToList(), block) };
        }
        return Branches(branches, elseBody, guard, cmds, rest, cont, ctx);
    }

    List<Line> Branches(List<(List<List<Cond>> Dnf, List<Item> Body)> branches, List<Item>? elseBody,
        List<Cond> guard, List<Cmd> cmds, List<Item> rest, string? cont, MethodCtx ctx)
    {
        var after = NewBlock(rest, cont);
        var lines = new List<Line>();
        foreach (var (dnf, body) in branches)
            foreach (var term in dnf)
                lines.AddRange(Seq(body, guard.Concat(term).Distinct().ToList(), cmds, after));
        lines.AddRange(Seq(elseBody ?? new List<Item>(), guard, cmds, after));
        return lines;
    }

    // ---------- Schleifen ----------

    public const int MaxLoopIterations = 100;

    /// <summary>
    /// Rollt for (int i = A; i &lt; B; i++) mit konstanten Grenzen aus. Jeder Durchlauf wird eine Kopie
    /// des Rumpfs, in der i eine Konstante ist.
    /// </summary>
    List<Item> UnrollFor(ForStatementSyntax f, MethodCtx ctx)
    {
        const string form = "Nur for-Schleifen mit fester Anzahl sind möglich, z. B. for (int i = 0; i < 5; i++).";
        if (f.Declaration is not { Variables.Count: 1 } decl || f.Initializers.Count > 0
            || decl.Variables[0].Initializer is not { } init)
            throw new CompileException(f, form + " Die Schleifenvariable muss im for deklariert werden.");
        var name = decl.Variables[0].Identifier.Text;
        long start = ConstEval(init.Value, ctx) ?? throw new CompileException(init.Value, "Der Startwert muss konstant sein. " + form);

        // Bedingung: i < N, i <= N, i > N, i >= N, i != N (oder gespiegelt)
        if (f.Condition is not BinaryExpressionSyntax cond || CmpOp(cond.Kind()) is not { } op)
            throw new CompileException((SyntaxNode?)f.Condition ?? f, "Die Bedingung muss ein Vergleich mit der Schleifenvariable sein. " + form);
        ExpressionSyntax limitExpr;
        if (NameOf(Unwrap(cond.Left)) == name && Unwrap(cond.Left) is IdentifierNameSyntax)
            limitExpr = cond.Right;
        else if (NameOf(Unwrap(cond.Right)) == name && Unwrap(cond.Right) is IdentifierNameSyntax)
        {
            limitExpr = cond.Left;
            op = op switch { "<" => ">", ">" => "<", "<=" => ">=", ">=" => "<=", _ => op };
        }
        else
            throw new CompileException(cond, "Die Bedingung muss die Schleifenvariable mit einer Konstanten vergleichen. " + form);
        long limit = ConstEval(limitExpr, ctx) ?? throw new CompileException(limitExpr, "Die Grenze muss konstant sein. " + form);

        if (f.Incrementors.Count != 1)
            throw new CompileException(f, "Die Schleife braucht genau einen Schritt, z. B. i++ oder i += 2.");
        long step = Step(f.Incrementors[0], name, ctx);

        var values = new List<ushort>();
        for (long v = start; Compare((ushort)v, op, (ushort)limit) && v is >= 0 and <= ushort.MaxValue; v += step)
        {
            if (values.Count >= MaxLoopIterations)
                throw new CompileException(f, $"Die Schleife hat mehr als {MaxLoopIterations} Durchläufe – das wäre zu viel Code für den Stift.");
            values.Add((ushort)v);
        }
        return values.Select(v => (Item)new StmtItem(f.Statement, ctx.WithLoopValue(name, v))).ToList();
    }

    long Step(ExpressionSyntax inc, string name, MethodCtx ctx)
    {
        long sign;
        switch (inc)
        {
            case PostfixUnaryExpressionSyntax or PrefixUnaryExpressionSyntax
                when inc.Kind() is SyntaxKind.PostIncrementExpression or SyntaxKind.PreIncrementExpression
                    or SyntaxKind.PostDecrementExpression or SyntaxKind.PreDecrementExpression:
                var operand = inc is PostfixUnaryExpressionSyntax p ? p.Operand : ((PrefixUnaryExpressionSyntax)inc).Operand;
                if (NameOf(operand) != name)
                    break;
                return inc.Kind() is SyntaxKind.PostIncrementExpression or SyntaxKind.PreIncrementExpression ? 1 : -1;
            case AssignmentExpressionSyntax a when NameOf(a.Left) == name
                && a.Kind() is SyntaxKind.AddAssignmentExpression or SyntaxKind.SubtractAssignmentExpression:
                sign = a.Kind() == SyntaxKind.AddAssignmentExpression ? 1 : -1;
                var k = ConstEval(a.Right, ctx) ?? throw new CompileException(a.Right, "Die Schrittweite muss konstant sein.");
                if (k == 0)
                    throw new CompileException(a.Right, "Schrittweite 0 ergibt eine Endlosschleife.");
                return sign * k;
        }
        throw new CompileException(inc, $"Als Schritt sind nur {name}++, {name}--, {name} += k und {name} -= k möglich.");
    }

    /// <summary>Rollt foreach (var x in new[] { 1, 2, 3 }) mit konstanten Werten aus.</summary>
    List<Item> UnrollForEach(ForEachStatementSyntax f, MethodCtx ctx)
    {
        var elements = f.Expression switch
        {
            ImplicitArrayCreationExpressionSyntax a => a.Initializer.Expressions,
            ArrayCreationExpressionSyntax { Initializer: { } init } => init.Expressions,
            CollectionExpressionSyntax c => SyntaxFactory.SeparatedList(c.Elements.OfType<ExpressionElementSyntax>().Select(e => e.Expression)),
            _ => throw new CompileException(f.Expression, "foreach ist nur über feste Werte möglich, z. B. foreach (var x in new[] { 1, 2, 3 })."),
        };
        if (elements.Count > MaxLoopIterations)
            throw new CompileException(f, $"Mehr als {MaxLoopIterations} Werte – das wäre zu viel Code für den Stift.");
        var name = f.Identifier.Text;
        return elements.Select(e => (Item)new StmtItem(f.Statement, ctx.WithLoopValue(name,
            ConstEval(e, ctx) ?? throw new CompileException(e, "Die Werte müssen konstant sein.")))).ToList();
    }

    // ---------- Nachbearbeitung ----------

    /// <summary>
    /// Fasst inhaltsgleiche Hilfsskripte zusammen (entstehen z. B., wenn dieselbe Methode in mehreren
    /// [Oid]-Methoden eingesetzt wird) und spart so OID-Codes.
    /// </summary>
    void MergeDuplicates()
    {
        while (true)
        {
            var rename = new Dictionary<string, string>();
            var byContent = new Dictionary<string, string>();
            foreach (var (name, lines) in _scripts)
            {
                var key = string.Join("\n", lines.Select(FormatLine));
                if (!_helpers.Contains(name))
                    continue;
                if (byContent.TryGetValue(key, out var existing))
                    rename[name] = existing;
                else
                    byContent[key] = name;
            }
            if (rename.Count == 0)
                return;
            foreach (var name in rename.Keys)
                _scripts.Remove(name);
            foreach (var lines in _scripts.Values)
                foreach (var line in lines)
                    for (int i = 0; i < line.Cmds.Count; i++)
                        foreach (var (from, to) in rename)
                            if (line.Cmds[i].Text == $"J({from})")
                                line.Cmds[i] = new Cmd($"J({to})");
        }
    }

    void AddScript(string name, List<Line> lines)
    {
        // Zeilen nach einer bedingungslosen Zeile werden nie erreicht
        int firstUnconditional = lines.FindIndex(l => l.Conds.Count == 0);
        if (firstUnconditional >= 0)
            lines = lines.Take(firstUnconditional + 1).ToList();
        // leere bedingungslose Zeile am Ende bewirkt nichts
        if (lines.Count > 0 && lines[^1].Conds.Count == 0 && lines[^1].Cmds.Count == 0)
            lines.RemoveAt(lines.Count - 1);

        var result = new List<Line>();
        foreach (var line in lines)
        {
            if (line.Cmds.Count <= MaxCommandsPerLine)
            {
                result.Add(line);
                continue;
            }
            // zu viele Befehle: Rest in ein Hilfsskript auslagern
            var head = line.Cmds.Take(MaxCommandsPerLine - 1).ToList();
            var tail = line.Cmds.Skip(MaxCommandsPerLine - 1).ToList();
            var restName = $"{_entry}_{++_blockCounter}";
            _helpers.Add(restName);
            AddScript(restName, new List<Line> { new() { Cmds = tail } });
            head.Add(new Cmd($"J({restName})"));
            result.Add(new Line { Conds = line.Conds, Cmds = head });
        }

        if (!_scripts.TryAdd(name, result))
            throw new CompileException(null, $"Der Skriptname „{name}“ entsteht doppelt (Hilfsskripte heißen Methode_1, Methode_2 …). Bitte eine Methode umbenennen.");
    }
}
