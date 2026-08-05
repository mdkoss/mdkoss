using System.Globalization;
using System.Text;

namespace MDKOSS.Core.Flow;

/// <summary>
/// Minimal expression evaluator: literals, variables, comparisons, && || !, + - * /.
/// </summary>
public static class FlowExpr
{
    public static object? Eval(string? expression, Func<string, object?> resolve)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }

        var tokens = Tokenize(expression);
        var parser = new Parser(tokens, resolve);
        return parser.ParseExpression();
    }

    public static bool EvalBool(string? expression, Func<string, object?> resolve)
    {
        var v = Eval(expression, resolve);
        return ToBool(v);
    }

    public static double EvalNumber(string? expression, Func<string, object?> resolve)
    {
        var v = Eval(expression, resolve);
        return ToNumber(v);
    }

    public static bool ToBool(object? v) => v switch
    {
        null => false,
        bool b => b,
        double d => Math.Abs(d) > double.Epsilon,
        long l => l != 0,
        int i => i != 0,
        string s when bool.TryParse(s, out var b) => b,
        string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => Math.Abs(d) > double.Epsilon,
        _ => false,
    };

    public static double ToNumber(object? v) => v switch
    {
        null => 0,
        double d => d,
        float f => f,
        int i => i,
        long l => l,
        bool b => b ? 1 : 0,
        string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        _ => Convert.ToDouble(v, CultureInfo.InvariantCulture),
    };

    public static string ToStringValue(object? v) => v switch
    {
        null => "",
        string s => s,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture) ?? "",
        _ => v.ToString() ?? "",
    };

    private enum TokKind
    {
        Number, String, Ident, Op, LParen, RParen, End,
    }

    private readonly record struct Tok(TokKind Kind, string Text);

    private static List<Tok> Tokenize(string src)
    {
        var list = new List<Tok>();
        var i = 0;
        while (i < src.Length)
        {
            var c = src[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c is '"' or '\'')
            {
                var quote = c;
                i++;
                var sb = new StringBuilder();
                while (i < src.Length && src[i] != quote)
                {
                    sb.Append(src[i++]);
                }

                if (i < src.Length)
                {
                    i++;
                }

                list.Add(new Tok(TokKind.String, sb.ToString()));
                continue;
            }

            if (char.IsDigit(c) || (c == '.' && i + 1 < src.Length && char.IsDigit(src[i + 1])))
            {
                var start = i;
                i++;
                while (i < src.Length && (char.IsDigit(src[i]) || src[i] == '.'))
                {
                    i++;
                }

                list.Add(new Tok(TokKind.Number, src[start..i]));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                i++;
                while (i < src.Length && (char.IsLetterOrDigit(src[i]) || src[i] == '_' || src[i] == '.'))
                {
                    i++;
                }

                list.Add(new Tok(TokKind.Ident, src[start..i]));
                continue;
            }

            if (c == '(')
            {
                list.Add(new Tok(TokKind.LParen, "("));
                i++;
                continue;
            }

            if (c == ')')
            {
                list.Add(new Tok(TokKind.RParen, ")"));
                i++;
                continue;
            }

            // multi-char ops
            if (i + 1 < src.Length)
            {
                var two = src[i..(i + 2)];
                if (two is "==" or "!=" or "<=" or ">=" or "&&" or "||")
                {
                    list.Add(new Tok(TokKind.Op, two));
                    i += 2;
                    continue;
                }
            }

            if ("+-*/<>!".Contains(c))
            {
                list.Add(new Tok(TokKind.Op, c.ToString()));
                i++;
                continue;
            }

            throw new InvalidOperationException($"Unexpected character '{c}' in expression.");
        }

        list.Add(new Tok(TokKind.End, ""));
        return list;
    }

    private sealed class Parser
    {
        private readonly List<Tok> _tokens;
        private readonly Func<string, object?> _resolve;
        private int _pos;

        public Parser(List<Tok> tokens, Func<string, object?> resolve)
        {
            _tokens = tokens;
            _resolve = resolve;
        }

        private Tok Peek() => _tokens[_pos];
        private Tok Next() => _tokens[_pos++];

        public object? ParseExpression()
        {
            var v = ParseOr();
            if (Peek().Kind != TokKind.End)
            {
                throw new InvalidOperationException($"Unexpected token '{Peek().Text}'");
            }

            return v;
        }

        private object? ParseOr()
        {
            var left = ParseAnd();
            while (Peek().Kind == TokKind.Op && Peek().Text == "||")
            {
                Next();
                var right = ParseAnd();
                left = ToBool(left) || ToBool(right);
            }

            return left;
        }

        private object? ParseAnd()
        {
            var left = ParseEquality();
            while (Peek().Kind == TokKind.Op && Peek().Text == "&&")
            {
                Next();
                var right = ParseEquality();
                left = ToBool(left) && ToBool(right);
            }

            return left;
        }

        private object? ParseEquality()
        {
            var left = ParseCompare();
            while (Peek().Kind == TokKind.Op && Peek().Text is "==" or "!=")
            {
                var op = Next().Text;
                var right = ParseCompare();
                var eq = ValuesEqual(left, right);
                left = op == "==" ? eq : !eq;
            }

            return left;
        }

        private object? ParseCompare()
        {
            var left = ParseAdd();
            while (Peek().Kind == TokKind.Op && Peek().Text is "<" or ">" or "<=" or ">=")
            {
                var op = Next().Text;
                var right = ParseAdd();
                var a = ToNumber(left);
                var b = ToNumber(right);
                left = op switch
                {
                    "<" => a < b,
                    ">" => a > b,
                    "<=" => a <= b,
                    ">=" => a >= b,
                    _ => false,
                };
            }

            return left;
        }

        private object? ParseAdd()
        {
            var left = ParseMul();
            while (Peek().Kind == TokKind.Op && Peek().Text is "+" or "-")
            {
                var op = Next().Text;
                var right = ParseMul();
                // string concat if either side is string and op is +
                if (op == "+" && (left is string || right is string))
                {
                    left = ToStringValue(left) + ToStringValue(right);
                }
                else
                {
                    var a = ToNumber(left);
                    var b = ToNumber(right);
                    left = op == "+" ? a + b : a - b;
                }
            }

            return left;
        }

        private object? ParseMul()
        {
            var left = ParseUnary();
            while (Peek().Kind == TokKind.Op && Peek().Text is "*" or "/")
            {
                var op = Next().Text;
                var right = ParseUnary();
                var a = ToNumber(left);
                var b = ToNumber(right);
                left = op == "*" ? a * b : (b == 0 ? double.NaN : a / b);
            }

            return left;
        }

        private object? ParseUnary()
        {
            if (Peek().Kind == TokKind.Op && Peek().Text == "!")
            {
                Next();
                return !ToBool(ParseUnary());
            }

            if (Peek().Kind == TokKind.Op && Peek().Text == "-")
            {
                Next();
                return -ToNumber(ParseUnary());
            }

            return ParsePrimary();
        }

        private object? ParsePrimary()
        {
            var t = Peek();
            if (t.Kind == TokKind.Number)
            {
                Next();
                return double.Parse(t.Text, CultureInfo.InvariantCulture);
            }

            if (t.Kind == TokKind.String)
            {
                Next();
                return t.Text;
            }

            if (t.Kind == TokKind.Ident)
            {
                Next();
                if (string.Equals(t.Text, "true", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (string.Equals(t.Text, "false", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (string.Equals(t.Text, "null", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                return _resolve(t.Text);
            }

            if (t.Kind == TokKind.LParen)
            {
                Next();
                var inner = ParseOr();
                if (Peek().Kind != TokKind.RParen)
                {
                    throw new InvalidOperationException("Expected ')'");
                }

                Next();
                return inner;
            }

            throw new InvalidOperationException($"Unexpected token '{t.Text}'");
        }

        private static bool ValuesEqual(object? a, object? b)
        {
            if (a is null && b is null)
            {
                return true;
            }

            if (a is null || b is null)
            {
                return false;
            }

            if (a is string || b is string)
            {
                return string.Equals(ToStringValue(a), ToStringValue(b), StringComparison.Ordinal);
            }

            if (a is bool || b is bool)
            {
                return ToBool(a) == ToBool(b);
            }

            return Math.Abs(ToNumber(a) - ToNumber(b)) < 1e-9;
        }
    }
}
