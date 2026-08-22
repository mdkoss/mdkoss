using System.Globalization;
using System.Text;

namespace MDKOSS.Iec61131;

/// <summary>Translates Flow expressions to IEC 61131-3 Structured Text.</summary>
public static class IecExpr
{
    public static string ToSt(string? expression, Func<string, string>? mapIdent = null)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return "FALSE";
        }

        var tokens = Tokenize(expression);
        var sb = new StringBuilder();
        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (i > 0)
            {
                sb.Append(' ');
            }

            sb.Append(t.Kind switch
            {
                TokKind.Number => NormalizeNumber(t.Text),
                TokKind.String => ToStString(t.Text),
                TokKind.Ident => MapIdent(t.Text, mapIdent),
                TokKind.Op => MapOp(t.Text),
                _ => t.Text,
            });
        }

        return sb.ToString();
    }

    public static string ToStString(string raw)
    {
        var escaped = (raw ?? "").Replace("'", "''", StringComparison.Ordinal);
        return $"'{escaped}'";
    }

    public static string Literal(object? value) => value switch
    {
        null => "0.0",
        bool b => b ? "TRUE" : "FALSE",
        string s => ToStString(s),
        IFormattable f => NormalizeNumber(f.ToString(null, CultureInfo.InvariantCulture) ?? "0"),
        _ => ToStString(value.ToString() ?? ""),
    };

    public static string TimeFromMs(int ms)
    {
        var n = Math.Max(0, ms);
        return $"T#{n.ToString(CultureInfo.InvariantCulture)}MS";
    }

    private static string MapIdent(string ident, Func<string, string>? mapIdent)
    {
        if (ident.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return "TRUE";
        }

        if (ident.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return "FALSE";
        }

        return mapIdent is null ? IecNames.Sanitize(ident) : mapIdent(ident);
    }

    private static string MapOp(string op) => op switch
    {
        "&&" => "AND",
        "||" => "OR",
        "!" => "NOT",
        "==" => "=",
        "!=" => "<>",
        _ => op,
    };

    private static string NormalizeNumber(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "0.0";
        }

        if (text.Contains('e', StringComparison.OrdinalIgnoreCase)
            || text.Contains('.', StringComparison.Ordinal))
        {
            return text;
        }

        return text + ".0";
    }

    private enum TokKind
    {
        Number, String, Ident, Op, LParen, RParen,
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
                var inner = new StringBuilder();
                while (i < src.Length && src[i] != quote)
                {
                    inner.Append(src[i++]);
                }

                if (i < src.Length)
                {
                    i++;
                }

                list.Add(new Tok(TokKind.String, inner.ToString()));
                continue;
            }

            if (char.IsDigit(c) || (c == '.' && i + 1 < src.Length && char.IsDigit(src[i + 1])))
            {
                var start = i;
                i++;
                while (i < src.Length && (char.IsDigit(src[i]) || src[i] == '.' || src[i] is 'e' or 'E'))
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

            throw new InvalidOperationException($"Unexpected character '{c}' in expression '{src}'.");
        }

        return list;
    }
}
