using System.Diagnostics;
using System.Reflection;

namespace OdinVS.Build.Tests;

internal sealed record MsBuildResult(int ExitCode, string Output);

// Tier-3 harness. Packs the current Odin.Sdk source into a scratch feed (never the local nuget/, which may be stale),
// then builds copies of tests/samples with `dotnet msbuild` using a scratch NuGet cache so same-version repacks are never served stale.
internal sealed class MsBuildSandbox
{
    private static readonly Lazy<MsBuildSandbox> m_Instance = new(() => new MsBuildSandbox());

    private readonly string m_Root = Path.Combine(Path.GetTempPath(), "odin-vs-build-tests", Guid.NewGuid().ToString("N"));

    private MsBuildSandbox()
    {
        Directory.CreateDirectory(m_Root);
        string feed = Path.Combine(m_Root, "feed");
        MsBuildResult pack = Run("dotnet", $"pack \"{Path.Combine(RepoRoot, "src", "OdinVS.Build", "Odin.Sdk.csproj")}\" -c Release -p:PackageOutputPath=\"{feed}\"", RepoRoot, new Dictionary<string, string>());
        if (pack.ExitCode != 0)
        {
            throw new InvalidOperationException($"packing Odin.Sdk failed: {pack.Output}");
        }

        PackagesDirectory = Path.Combine(m_Root, "packages");
        FeedDirectory = feed;
    }

    public static MsBuildSandbox Instance => m_Instance.Value;

    public string FeedDirectory { get; }

    // Removes this run's scratch folder (feed, package cache, sample copies) so %TEMP% does not grow with every test run.
    public static void CleanUp()
    {
        if (!m_Instance.IsValueCreated)
        {
            return;
        }

        try
        {
            Directory.Delete(m_Instance.Value.m_Root, true);
        }
        catch (IOException)
        {
            // Best-effort: a lingering msbuild node can hold a file; the folder is under %TEMP% and harmless to leave.
        }
    }

    public string PackagesDirectory { get; }

    private static string RepoRoot { get; } = typeof(MsBuildSandbox).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .First(attribute => attribute.Key is "RepoRoot").Value!;

    // Copies tests/samples/<sample> to a fresh directory next to a nuget.config that points only at the scratch feed.
    public string CopySample(string sample, string? parentFolder = null)
    {
        string workDirectory = Path.Combine(m_Root, "work", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDirectory);
        File.WriteAllText(
            Path.Combine(workDirectory, "nuget.config"),
            $"""<configuration><packageSources><clear /><add key="scratch" value="{FeedDirectory}" /></packageSources></configuration>""");
        string source = Path.Combine(RepoRoot, "tests", "samples", sample);
        string destination = Path.Combine(workDirectory, parentFolder ?? string.Empty, sample);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, file);
            if (relative.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal) || relative.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(destination, relative))!);
            File.Copy(file, Path.Combine(destination, relative));
        }

        return destination;
    }

    // Evaluates the project without building and prints the named item (JSON), the way a static evaluation sees it.
    public MsBuildResult GetItem(string projectDirectory, string projectFile, string item)
    {
        return Run(
            "dotnet",
            $"msbuild \"{Path.Combine(projectDirectory, projectFile)}\" -restore -getItem:{item} -nodeReuse:false -nologo",
            projectDirectory,
            new Dictionary<string, string> { ["NUGET_PACKAGES"] = PackagesDirectory });
    }

    // Evaluates the project without building and prints the named properties (JSON).
    public MsBuildResult GetProperties(string projectDirectory, string projectFile, params string[] properties)
    {
        return Run(
            "dotnet",
            $"msbuild \"{Path.Combine(projectDirectory, projectFile)}\" -restore -getProperty:{string.Join(",", properties)} -nodeReuse:false -nologo",
            projectDirectory,
            new Dictionary<string, string> { ["NUGET_PACKAGES"] = PackagesDirectory });
    }

    public MsBuildResult Build(string projectDirectory, string projectFile, string targets, params string[] properties)
    {
        string propertyArguments = string.Concat(properties.Select(p => $" -p:\"{p}\""));
        return Run(
            "dotnet",
            $"msbuild \"{Path.Combine(projectDirectory, projectFile)}\" -restore -t:{targets} -nodeReuse:false -nologo -v:n{propertyArguments}",
            projectDirectory,
            new Dictionary<string, string> { ["NUGET_PACKAGES"] = PackagesDirectory });
    }

    private static MsBuildResult Run(string fileName, string arguments, string workingDirectory, IDictionary<string, string> environment)
    {
        ProcessStartInfo startInfo = new(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (KeyValuePair<string, string> pair in environment)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        using Process process = Process.Start(startInfo)!;
        Task<string> errors = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return new MsBuildResult(process.ExitCode, output + errors.Result);
    }
}
