using System.Text;

namespace OdinVS.Core;

// Text handling for Watch expressions on Odin containers, used by the debugger component (src/OdinVS.Debugger). Odin indexes a
// slice, a dynamic array or a string where C indexes a pointer, so `a[i]` is rewritten to `a.data[i]`; the rewrite must also check
// `i` against `a.len` (the native evaluator would read past the end), and `m[key]` on a map needs the key text split out.
public static class OdinWatchExpressions
{
    // One `.data` insertion: the postfix expression it was inserted after and the text of the index.
    public sealed record IndexCheck(string Base, string Index);

    // `Text` is the rewritten expression; `Checks` has one entry per inserted `.data`, left to right.
    public sealed record IndexRewrite(string Text, IReadOnlyList<IndexCheck> Checks);

    // `name[key]rest`: `Base` is a plain (bracket-free) member chain, `Index` the text between the first bracket pair, `Rest` what follows.
    public sealed record MapLookupShape(string Base, string Index, string Rest);

    public static IReadOnlyList<int> FindBrackets(string text, int maximum)
    {
        List<int> brackets = [];
        for (int i = 0; i < text.Length && brackets.Count < maximum; i++)
        {
            if (text[i] == '[')
            {
                brackets.Add(i);
            }
        }

        return brackets;
    }

    // Inserts `.data` before the brackets selected by `mask` (bit b is the b-th entry of `brackets`).
    public static IndexRewrite Rewrite(string text, IReadOnlyList<int> brackets, int mask)
    {
        StringBuilder rewritten = new(text);
        List<int> inserted = [];
        int shift = 0;
        for (int b = 0; b < brackets.Count; b++)
        {
            if ((mask & (1 << b)) == 0)
            {
                continue;
            }

            int at = brackets[b] + shift;
            rewritten.Insert(at, ".data");
            inserted.Add(at);
            shift += ".data".Length;
        }

        string result = rewritten.ToString();
        List<IndexCheck> checks = [];
        foreach (int at in inserted)
        {
            int open = at + ".data".Length;
            int close = MatchingClose(result, open);
            if (close < 0)
            {
                continue;
            }

            string baseText = result.Substring(BaseStart(result, at), at - BaseStart(result, at)).Trim();
            string indexText = result.Substring(open + 1, close - open - 1).Trim();
            if (baseText.Length > 0 && indexText.Length > 0)
            {
                checks.Add(new IndexCheck(baseText, indexText));
            }
        }

        return new IndexRewrite(result, checks);
    }

    public static MapLookupShape? SplitLookup(string text)
    {
        int open = text.IndexOf('[');
        if (open <= 0)
        {
            return null;
        }

        string baseText = text.Substring(0, open).Trim();
        if (!IsMemberChain(baseText))
        {
            return null;
        }

        int close = MatchingClose(text, open);
        if (close < 0)
        {
            return null;
        }

        string index = text.Substring(open + 1, close - open - 1).Trim();
        return index.Length == 0 ? null : new MapLookupShape(baseText, index, text.Substring(close + 1));
    }

    // `"red"` as the bytes Odin would store; false for anything that is not a plain string literal (a variable, a call).
    public static bool TryParseStringLiteral(string text, out byte[] bytes)
    {
        bytes = [];
        string trimmed = text.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '"' || trimmed[trimmed.Length - 1] != '"')
        {
            return false;
        }

        List<byte> result = [];
        for (int i = 1; i < trimmed.Length - 1; i++)
        {
            char c = trimmed[i];
            if (c == '"')
            {
                return false;
            }

            if (c != '\\')
            {
                // A character outside the Basic Multilingual Plane is two UTF-16 chars and must be encoded together.
                int length = char.IsHighSurrogate(c) && i + 1 < trimmed.Length - 1 && char.IsLowSurrogate(trimmed[i + 1]) ? 2 : 1;
                result.AddRange(Encoding.UTF8.GetBytes(trimmed.Substring(i, length)));
                i += length - 1;
                continue;
            }

            if (++i >= trimmed.Length - 1)
            {
                return false;
            }

            switch (trimmed[i])
            {
                case 'n':
                    result.Add((byte)'\n');
                    break;
                case 't':
                    result.Add((byte)'\t');
                    break;
                case 'r':
                    result.Add((byte)'\r');
                    break;
                case '0':
                    result.Add(0);
                    break;
                case '\\':
                    result.Add((byte)'\\');
                    break;
                case '"':
                    result.Add((byte)'"');
                    break;
                case '\'':
                    result.Add((byte)'\'');
                    break;
                default:
                    return false;
            }
        }

        bytes = [.. result];
        return true;
    }

    // The low `size` bytes of `value`, little-endian (a map key of that size, as stored in the debuggee).
    public static byte[] LittleEndian(long value, int size)
    {
        byte[] bytes = new byte[size];
        for (int i = 0; i < size; i++)
        {
            bytes[i] = i < 8 ? (byte)((ulong)value >> (8 * i)) : (byte)(value < 0 ? 0xFF : 0);
        }

        return bytes;
    }

    private static bool IsMemberChain(string text)
    {
        if (text.Length == 0 || !(char.IsLetter(text[0]) || text[0] == '_'))
        {
            return false;
        }

        return text.All(c => char.IsLetterOrDigit(c) || c is '_' or '.' or ':');
    }

    // Index of the `]` that closes the `[` at `open`, or -1. Brackets inside a string literal do not count.
    private static int MatchingClose(string text, int open)
    {
        int depth = 0;
        bool inString = false;
        for (int i = open; i < text.Length; i++)
        {
            if (inString)
            {
                if (text[i] == '\\')
                {
                    i++;
                }
                else if (text[i] == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (text[i] == '"')
            {
                inString = true;
            }
            else if (text[i] == '[')
            {
                depth++;
            }
            else if (text[i] == ']')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    // Start of the postfix expression (`a.b[1].c`, `(*p)`, `f(x)`) that ends just before `end`.
    private static int BaseStart(string text, int end)
    {
        int i = end - 1;
        while (i >= 0)
        {
            char c = text[i];
            if (char.IsLetterOrDigit(c) || c is '_' or '.' or ':')
            {
                i--;
            }
            else if (c == '>' && i > 0 && text[i - 1] == '-')
            {
                i -= 2;
            }
            else if (c is ']' or ')')
            {
                char opener = c == ']' ? '[' : '(';
                int depth = 0;
                int j = i;
                for (; j >= 0; j--)
                {
                    if (text[j] == c)
                    {
                        depth++;
                    }
                    else if (text[j] == opener)
                    {
                        depth--;
                        if (depth == 0)
                        {
                            break;
                        }
                    }
                }

                if (j < 0)
                {
                    break;
                }

                i = j - 1;
            }
            else
            {
                break;
            }
        }

        return i + 1;
    }
}
