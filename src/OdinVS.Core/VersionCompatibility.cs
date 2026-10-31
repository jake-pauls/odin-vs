namespace OdinVS.Core;

internal static class VersionCompatibility
{
    // Null when the versions match; otherwise a warning to log.
    public static string? Check(string? odinVersionText, OdinVersion olsVersion)
    {
        OdinVersion? odin = OdinVersion.Parse(odinVersionText);
        if (odin is null)
        {
            return $"Could not determine the installed Odin version ('{odinVersionText}'); skipping compatibility check.";
        }

        return odin == olsVersion
            ? null
            : $"Bundled ols {olsVersion} does not match installed odin {odin}; completion and diagnostics may be wrong.";
    }
}
