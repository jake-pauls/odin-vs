using System.Reflection;

namespace OdinVS.Core.Tests;

internal static class RepoPaths
{
    public static string Root { get; } = ReadRoot();

    public static string BundledOlsDirectory => Path.Combine(Root, "src", "OdinVS", "ols");

    private static string ReadRoot()
    {
        string? root = typeof(RepoPaths).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key is "RepoRoot")?.Value;
        if (root is null || !Directory.Exists(root))
        {
            throw new InvalidOperationException($"repo root '{root}' stamped by OdinVS.Core.Tests.csproj does not exist; rebuild the test project from the repo it lives in");
        }

        return root;
    }
}
