namespace OdinVS.Core.Tests;

internal static class RepoPaths
{
    public static string Root { get; } = FindRoot();

    public static string BundledOlsDirectory => Path.Combine(Root, "src", "OdinVS", "ols");

    private static string FindRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CLAUDE.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
