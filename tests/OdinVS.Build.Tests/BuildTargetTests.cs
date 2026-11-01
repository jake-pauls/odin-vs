using System.Diagnostics;
using System.Text.RegularExpressions;

namespace OdinVS.Build.Tests;

// These run real odin (and its MSVC link step), so they need the toolchain from docs/toolchain.md. `dotnet msbuild` is used here;
// the same targets were also run with VS's MSBuild.exe by hand (see docs/milestones.md).
[TestClass]
[TestCategory("RequiresToolchain")]
public sealed class BuildTargetTests
{
    private static string Exe(string project, string configuration, string extension = "exe") =>
        Path.Combine(project, "bin", configuration, $"{Path.GetFileName(project)}.{extension}");

    private static (string Directory, MsBuildResult Result) BuildSample(string sample, string targets = "Build", params string[] properties)
    {
        string directory = MsBuildSandbox.Instance.CopySample(sample);
        return (directory, MsBuildSandbox.Instance.Build(directory, sample + ".odinproj", targets, properties));
    }

    [TestMethod]
    public void DebugBuildProducesRunnableExeAndPdb()
    {
        (string project, MsBuildResult result) = BuildSample("HelloOdin");
        Assert.AreEqual(0, result.ExitCode, result.Output);
        Assert.IsTrue(File.Exists(Exe(project, "Debug")), result.Output);
        Assert.IsTrue(File.Exists(Exe(project, "Debug", "pdb")), "Debug must emit a PDB");

        using Process run = Process.Start(new ProcessStartInfo(Exe(project, "Debug")) { RedirectStandardOutput = true, UseShellExecute = false })!;
        string stdout = run.StandardOutput.ReadToEnd();
        run.WaitForExit();
        StringAssert.Contains(stdout, "total: 10 odin");
    }

    [TestMethod]
    public void ReleaseBuildHasNoDebugFlagAndNoPdb()
    {
        (string project, MsBuildResult result) = BuildSample("HelloOdin", "Build", "Configuration=Release");
        Assert.AreEqual(0, result.ExitCode, result.Output);
        Assert.IsTrue(File.Exists(Exe(project, "Release")), result.Output);
        Assert.IsFalse(File.Exists(Exe(project, "Release", "pdb")), "Release must not emit a PDB");
        Assert.DoesNotContain(" -debug ", result.Output);
    }

    [TestMethod]
    public void CleanRemovesExeAndPdb()
    {
        (string project, MsBuildResult build) = BuildSample("HelloOdin");
        Assert.AreEqual(0, build.ExitCode, build.Output);
        MsBuildResult clean = MsBuildSandbox.Instance.Build(project, "HelloOdin.odinproj", "Clean");
        Assert.AreEqual(0, clean.ExitCode, clean.Output);
        Assert.IsFalse(File.Exists(Exe(project, "Debug")));
        Assert.IsFalse(File.Exists(Exe(project, "Debug", "pdb")));
    }

    [TestMethod]
    public void RebuildReplacesTheOutputs()
    {
        (string project, MsBuildResult build) = BuildSample("HelloOdin");
        Assert.AreEqual(0, build.ExitCode, build.Output);
        File.SetLastWriteTimeUtc(Exe(project, "Debug"), new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        MsBuildResult rebuild = MsBuildSandbox.Instance.Build(project, "HelloOdin.odinproj", "Rebuild");
        Assert.AreEqual(0, rebuild.ExitCode, rebuild.Output);
        Assert.IsGreaterThan(new DateTime(2000, 1, 1), File.GetLastWriteTime(Exe(project, "Debug")));
        Assert.IsTrue(File.Exists(Exe(project, "Debug", "pdb")));
    }

    [TestMethod]
    public void SecondBuildIsSkippedUntilASourceChanges()
    {
        (string project, MsBuildResult first) = BuildSample("HelloOdin");
        Assert.AreEqual(0, first.ExitCode, first.Output);
        StringAssert.Contains(first.Output, "ODIN: ");

        MsBuildResult second = MsBuildSandbox.Instance.Build(project, "HelloOdin.odinproj", "Build");
        Assert.AreEqual(0, second.ExitCode, second.Output);
        Assert.DoesNotContain("ODIN: ", second.Output, "an up-to-date build must not run odin");

        string source = Path.Combine(project, "src", "main.odin");
        File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(1));
        MsBuildResult third = MsBuildSandbox.Instance.Build(project, "HelloOdin.odinproj", "Build");
        Assert.AreEqual(0, third.ExitCode, third.Output);
        StringAssert.Contains(third.Output, "ODIN: ");
    }

    [TestMethod]
    public void CompileErrorsAreCanonicalMsBuildErrors()
    {
        (string project, MsBuildResult result) = BuildSample("BrokenOdin");
        Assert.AreNotEqual(0, result.ExitCode, result.Output);
        string main = Path.Combine(project, "src", "main.odin");
        Assert.IsTrue(
            Regex.IsMatch(result.Output, Regex.Escape(main) + @"\(6,11,6,17\): error ODIN0001: Cannot convert"),
            result.Output);
        Assert.IsTrue(
            Regex.IsMatch(result.Output, Regex.Escape(main) + @"\(7,14,7,15\): error ODIN0001: Undeclared name: y"),
            result.Output);
        Assert.IsFalse(File.Exists(Exe(project, "Debug")));
    }

    // CPS reads ProjectConfiguration metadata unevaluated (the Configuration Manager showed "$(Platform)" as text in Exp, 2026-11-01),
    // so the metadata must contain no $( or %( at all.
    [TestMethod]
    public void ProjectConfigurationsAreLiteralDebugAndReleaseX64()
    {
        string project = MsBuildSandbox.Instance.CopySample("HelloOdin");
        MsBuildResult result = MsBuildSandbox.Instance.GetItem(project, "HelloOdin.odinproj", "ProjectConfiguration");
        Assert.AreEqual(0, result.ExitCode, result.Output);
        StringAssert.Contains(result.Output, "\"Identity\": \"Debug|x64\"");
        StringAssert.Contains(result.Output, "\"Identity\": \"Release|x64\"");
        StringAssert.Contains(result.Output, "\"Platform\": \"x64\"");

        string sdkVersionDirectory = Directory.GetDirectories(Path.Combine(MsBuildSandbox.Instance.PackagesDirectory, "odin.sdk")).Single();
        string unevaluated = File.ReadAllText(Path.Combine(sdkVersionDirectory, "Sdk", "Sdk.targets"));
        Assert.IsFalse(
            Regex.IsMatch(unevaluated, @"<ProjectConfiguration[^>]*>(?:(?!</ProjectConfiguration>).)*(\$\(|%\()", RegexOptions.Singleline),
            "ProjectConfiguration metadata in Sdk.targets must be literal");
    }

    [TestMethod]
    public void CompileWarningsAreCanonicalAndDoNotFailTheBuild()
    {
        (string project, MsBuildResult result) = BuildSample("WarnOdin");
        Assert.AreEqual(0, result.ExitCode, result.Output);
        string main = Path.Combine(project, "src", "main.odin");
        Assert.IsTrue(
            Regex.IsMatch(result.Output, Regex.Escape(main) + @"\(11,2,11,11\): warning ODIN0002: old_thing is deprecated: use new_thing instead"),
            result.Output);
        Assert.IsTrue(File.Exists(Exe(project, "Debug")), "a warning must not stop the exe from being produced");
    }

    [TestMethod]
    public void MissingOdinIsReportedWithoutRunningAnything()
    {
        string missing = Path.Combine(Path.GetTempPath(), "no-such-odin", "odin.exe");
        (string project, MsBuildResult result) = BuildSample("HelloOdin", "Build", "OdinExe=" + missing);
        Assert.AreNotEqual(0, result.ExitCode, result.Output);
        StringAssert.Contains(result.Output, "error ODIN0101");
        StringAssert.Contains(result.Output, missing);
        Assert.IsFalse(File.Exists(Exe(project, "Debug")));
    }
}
