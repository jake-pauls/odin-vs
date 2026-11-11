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

    // Positions of the first `maximum` `[` in `text`, not counting brackets inside string literals.
    public static IReadOnlyList<int> FindBrackets(string text, int maximum)
    {
        List<int> brackets = [];
        bool inString = false;
        for (int i = 0; i < text.Length && brackets.Count < maximum; i++)
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
                brackets.Add(i);
            }
        }

        return brackets;
    }

    // `prefix + tail`, as typed and then with `.data` inserted before each non-empty subset of the brackets of `tail` (the prefix is
    // never touched: it is an evaluator-built cast), fewest insertions first. The first entry is the text unchanged. Used for the
    // text after a map lookup and for a map key that contains indexes.
    public static IReadOnlyList<IndexRewrite> Variants(string prefix, string tail, int maxBrackets)
    {
        string full = prefix + tail;
        List<int> brackets = [.. FindBrackets(tail, maxBrackets).Select(b => b + prefix.Length)];
        List<IndexRewrite> variants = [new IndexRewrite(full, [])];
        foreach (int mask in SubsetOrder(brackets.Count))
        {
            variants.Add(Rewrite(full, brackets, mask));
        }

        return variants;
    }

    // `len(x)` and `cap(x)` anywhere in the text become `x.len` and `x.cap` (Odin's builtins are members in the debug info); the
    // argument is parenthesised unless it is already one postfix expression. Each call is rewritten by balanced parentheses, so
    // `len(a) + len(b)` and `len(m[len(k)])` work. `strlen(x)` and `a.len(x)` are not builtins and are left alone.
    public static string RewriteBuiltins(string text)
    {
        StringBuilder result = new();
        int i = 0;
        while (i < text.Length)
        {
            int nameLength = BuiltinNameLength(text, i);
            if (nameLength > 0)
            {
                int open = i + nameLength;
                while (open < text.Length && text[open] == ' ')
                {
                    open++;
                }

                int close = open < text.Length && text[open] == '(' ? MatchingParenthesis(text, open) : -1;
                if (close > 0)
                {
                    string argument = RewriteBuiltins(text.Substring(open + 1, close - open - 1)).Trim();
                    if (argument.Length > 0)
                    {
                        result.Append(BaseStart(argument, argument.Length) == 0 ? argument : "(" + argument + ")");
                        result.Append('.').Append(text, i, 3);
                        i = close + 1;
                        continue;
                    }
                }
            }

            result.Append(text[i]);
            i++;
        }

        return result.ToString();
    }

    private static int BuiltinNameLength(string text, int at)
    {
        if (at + 3 > text.Length || !(string.CompareOrdinal(text, at, "len", 0, 3) == 0 || string.CompareOrdinal(text, at, "cap", 0, 3) == 0))
        {
            return 0;
        }

        char before = at > 0 ? text[at - 1] : ' ';
        return char.IsLetterOrDigit(before) || before is '_' or '.' ? 0 : 3;
    }

    private static int MatchingParenthesis(string text, int open)
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
            else if (text[i] == '(')
            {
                depth++;
            }
            else if (text[i] == ')')
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
            // An index with side effects (`i++`, a call) would be evaluated again by the check, so it is not checked.
            if (baseText.Length > 0 && indexText.Length > 0 && !HasSideEffects(indexText))
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

    // `[4]int` (Odin's spelling) or `int[4]` (the C spelling): the length of the first dimension, or null for any other type.
    public static long? FixedArrayLength(string type)
    {
        string trimmed = type.Trim();
        if (trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            int close = trimmed.IndexOf(']');
            return close > 1 && long.TryParse(trimmed.Substring(1, close - 1), out long odinLength) ? odinLength : null;
        }

        int open = trimmed.LastIndexOf('[');
        if (open <= 0 || !trimmed.EndsWith("]", StringComparison.Ordinal) || trimmed.IndexOf('[') != open)
        {
            return null;
        }

        return long.TryParse(trimmed.Substring(open + 1, trimmed.Length - open - 2), out long cLength) ? cLength : null;
    }

    // The order in which the subsets of `count` brackets are tried (bit b selects bracket b, none excluded): fewest rewrites
    // first, then by value, so the smallest rewrite that evaluates wins.
    public static IReadOnlyList<int> SubsetOrder(int count)
    {
        List<int> masks = [];
        for (int mask = 1; mask < (1 << count); mask++)
        {
            masks.Add(mask);
        }

        masks.Sort((x, y) =>
        {
            int byCount = BitCount(x).CompareTo(BitCount(y));
            return byCount != 0 ? byCount : x.CompareTo(y);
        });
        return masks;
    }

    // The stored bytes of an integer map key of `size` bytes, or null when `value` is outside what that type can hold
    // (`m[300]` on a `map[u8]V` is not key 44). Eight-byte keys take any value: an unsigned one arrives as a negative `long`.
    public static byte[]? TryIntegerKey(long value, int size, bool isSigned)
    {
        if (size is < 1 or > 8)
        {
            return null;
        }

        if (size < 8)
        {
            long bound = 1L << (8 * size - (isSigned ? 1 : 0));
            long minimum = isSigned ? -bound : 0;
            if (value < minimum || value > bound - 1)
            {
                return null;
            }
        }

        return LittleEndian(value, size);
    }

    // True when evaluating the text more than once could change something: an assignment, `++`/`--`, or a call.
    public static bool HasSideEffects(string text)
    {
        if (text.Contains("++") || text.Contains("--"))
        {
            return true;
        }

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '(' && i > 0 && (char.IsLetterOrDigit(text[i - 1]) || text[i - 1] is '_' or ']' or ')'))
            {
                return true;
            }

            if (c == '=')
            {
                bool comparison = (i > 0 && text[i - 1] is '=' or '<' or '>' or '!') || (i + 1 < text.Length && text[i + 1] == '=');
                if (!comparison)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static int BitCount(int value)
    {
        int count = 0;
        for (; value != 0; value &= value - 1)
        {
            count++;
        }

        return count;
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
