namespace OdinVS.Core;

public static class OdinNumericLiteral
{
    // True when typing `typed` right after `textBeforeCaret` would only continue a number such as `1.5` or `0x` (so `1.5f` or `0xFF`).
    // VS would otherwise open a completion list of identifiers inside the literal.
    public static bool ContinuesNumber(string textBeforeCaret, char typed)
    {
        if (!IsTokenChar(typed))
        {
            return false;
        }

        int start = textBeforeCaret.Length;
        while (start > 0 && (IsTokenChar(textBeforeCaret[start - 1]) || textBeforeCaret[start - 1] is '.'))
        {
            start--;
        }

        return start < textBeforeCaret.Length && char.IsDigit(textBeforeCaret[start]);
    }

    private static bool IsTokenChar(char c) => char.IsLetterOrDigit(c) || c is '_';
}