namespace OdinVS.Core;

internal sealed record ResolvedTools(string? Ols, string? Odin, IReadOnlyList<string> Problems);

internal sealed class ToolResolver(ToolLocator locator)
{
    public const string OdinFallback = @"C:\Program Files\Odin\odin\dist\odin.exe";

    // ols ships inside the extension; odin is the user's own install (PATH first, then the default location).
    public ResolvedTools Resolve(string extensionDirectory, string? pathVariable)
    {
        string olsDirectory = Path.Combine(extensionDirectory, "ols");
        string? ols = locator.Find("ols.exe", [olsDirectory], []);
        string? odin = locator.Find("odin.exe", (pathVariable ?? string.Empty).Split(Path.PathSeparator), [OdinFallback]);

        List<string> problems = [];
        if (ols is null)
        {
            problems.Add($"ols.exe not found under {olsDirectory}");
        }

        if (odin is null)
        {
            problems.Add($"odin.exe not found on PATH or at {OdinFallback}");
        }

        return new ResolvedTools(ols, odin, problems);
    }
}
