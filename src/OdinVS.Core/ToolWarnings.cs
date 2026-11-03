namespace OdinVS.Core;

internal static class ToolWarnings
{
    // Messages to show the user when an .odinproj is open: each missing tool, and a mismatch between the installed
    // odin and the bundled ols. The version check only runs when both tools were found.
    public static IReadOnlyList<string> Compute(ResolvedTools tools, string? odinVersionText, OdinVersion bundledOls)
    {
        List<string> warnings = [.. tools.Problems];
        if (tools.Odin is not null && tools.Ols is not null)
        {
            string? mismatch = VersionCompatibility.Check(odinVersionText, bundledOls);
            if (mismatch is not null)
            {
                warnings.Add(mismatch);
            }
        }

        return warnings;
    }
}
