using System.Globalization;
using System.Text;

namespace OdinVS.Core;

// Reads the small JSON documents Odin prints (-json-errors). Objects become Dictionary<string, object?>, arrays List<object?>,
// numbers double. Core stays free of System.Text.Json so the build task loads in both VS MSBuild (net472) and dotnet msbuild.
internal sealed class MiniJson(string text)
{
    private int m_Index;

    public static bool TryParse(string text, out object? value)
    {
        try
        {
            MiniJson reader = new(text);
            reader.SkipWhitespace();
            value = reader.ReadValue();
            reader.SkipWhitespace();
            return reader.m_Index == text.Length;
        }
        catch (FormatException)
        {
            value = null;
            return false;
        }
    }

    private object? ReadValue()
    {
        if (m_Index >= text.Length)
        {
            throw new FormatException("unexpected end of JSON");
        }

        switch (text[m_Index])
        {
            case '{':
                return ReadObject();
            case '[':
                return ReadArray();
            case '"':
                return ReadString();
            case 't':
                Expect("true");
                return true;
            case 'f':
                Expect("false");
                return false;
            case 'n':
                Expect("null");
                return null;
            default:
                return ReadNumber();
        }
    }

    private Dictionary<string, object?> ReadObject()
    {
        Dictionary<string, object?> result = [];
        m_Index++;
        SkipWhitespace();
        if (Peek() == '}')
        {
            m_Index++;
            return result;
        }

        while (true)
        {
            SkipWhitespace();
            string key = ReadString();
            SkipWhitespace();
            ExpectChar(':');
            SkipWhitespace();
            result[key] = ReadValue();
            SkipWhitespace();
            if (Peek() == ',')
            {
                m_Index++;
                continue;
            }

            ExpectChar('}');
            return result;
        }
    }

    private List<object?> ReadArray()
    {
        List<object?> result = [];
        m_Index++;
        SkipWhitespace();
        if (Peek() == ']')
        {
            m_Index++;
            return result;
        }

        while (true)
        {
            SkipWhitespace();
            result.Add(ReadValue());
            SkipWhitespace();
            if (Peek() == ',')
            {
                m_Index++;
                continue;
            }

            ExpectChar(']');
            return result;
        }
    }

    private string ReadString()
    {
        ExpectChar('"');
        StringBuilder builder = new();
        while (true)
        {
            if (m_Index >= text.Length)
            {
                throw new FormatException("unterminated string");
            }

            char c = text[m_Index++];
            if (c == '"')
            {
                return builder.ToString();
            }

            if (c != '\\')
            {
                builder.Append(c);
                continue;
            }

            if (m_Index >= text.Length)
            {
                throw new FormatException("unterminated escape");
            }

            char escaped = text[m_Index++];
            switch (escaped)
            {
                case 'n':
                    builder.Append('\n');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case 'b':
                    builder.Append('\b');
                    break;
                case 'f':
                    builder.Append('\f');
                    break;
                case 'u':
                    if (m_Index + 4 > text.Length || !int.TryParse(text.Substring(m_Index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                    {
                        throw new FormatException("bad \\u escape");
                    }

                    builder.Append((char)code);
                    m_Index += 4;
                    break;
                default:
                    builder.Append(escaped);
                    break;
            }
        }
    }

    private double ReadNumber()
    {
        int start = m_Index;
        while (m_Index < text.Length && "+-0123456789.eE".IndexOf(text[m_Index]) >= 0)
        {
            m_Index++;
        }

        if (!double.TryParse(text.Substring(start, m_Index - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
        {
            throw new FormatException($"bad JSON value at {start}");
        }

        return number;
    }

    private void SkipWhitespace()
    {
        while (m_Index < text.Length && char.IsWhiteSpace(text[m_Index]))
        {
            m_Index++;
        }
    }

    private char Peek() => m_Index < text.Length ? text[m_Index] : '\0';

    private void ExpectChar(char expected)
    {
        if (Peek() != expected)
        {
            throw new FormatException($"expected '{expected}' at {m_Index}");
        }

        m_Index++;
    }

    private void Expect(string literal)
    {
        if (string.CompareOrdinal(text, m_Index, literal, 0, literal.Length) != 0)
        {
            throw new FormatException($"bad JSON literal at {m_Index}");
        }

        m_Index += literal.Length;
    }
}
