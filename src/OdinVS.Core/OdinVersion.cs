using System.Text.RegularExpressions;

namespace OdinVS.Core;

internal readonly record struct OdinVersion(int Year, int Month)
{
    private static readonly Regex m_Pattern = new(@"dev-([0-9]{4})-([0-9]{2})", RegexOptions.Compiled);

    // Accepts "dev-2026-10", "dev-2026-10-nightly:84bc3fc" or a line containing either.
    // The "dev-YYYY-MM" shape is Odin's rolling-release naming today and is likely to change when Odin reaches 1.0;
    // when it does, update m_Pattern here, ToString below, and the comparison in VersionCompatibility together.
    public static OdinVersion? Parse(string? text)
    {
        if (text is null)
        {
            return null;
        }

        Match match = m_Pattern.Match(text);
        return match.Success
            ? new OdinVersion(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value))
            : null;
    }

    public override string ToString() => $"dev-{Year:D4}-{Month:D2}";
}
