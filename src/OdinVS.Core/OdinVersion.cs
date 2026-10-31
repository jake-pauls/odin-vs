using System.Text.RegularExpressions;

namespace OdinVS.Core;

internal readonly record struct OdinVersion(int Year, int Month)
{
    private static readonly Regex Pattern = new(@"dev-([0-9]{4})-([0-9]{2})", RegexOptions.Compiled);

    // Accepts "dev-2026-10", "dev-2026-10-nightly:84bc3fc" or a line containing either.
    public static OdinVersion? Parse(string? text)
    {
        if (text is null)
        {
            return null;
        }

        Match match = Pattern.Match(text);
        return match.Success
            ? new OdinVersion(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value))
            : null;
    }

    public override string ToString() => $"dev-{Year:D4}-{Month:D2}";
}
