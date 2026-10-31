namespace OdinVS.Core;

internal static class VersionCompatibility
{
    // Null when the versions match; otherwise a warning to log.
    public static string? Check(string? odinVersionText, string? olsVersionText)
    {
        OdinVersion? odin = OdinVersion.Parse(odinVersionText);
        OdinVersion? ols = OdinVersion.Parse(olsVersionText);
        if (odin is null || ols is null)
        {
            return $"Could not determine Odin ('{odinVersionText}') or ols ('{olsVersionText}') version; skipping compatibility check.";
        }

        return odin == ols
            ? null
            : $"ols {ols} does not match odin {odin}; completion and diagnostics may be wrong. Use an ols built for the installed Odin.";
    }
}
