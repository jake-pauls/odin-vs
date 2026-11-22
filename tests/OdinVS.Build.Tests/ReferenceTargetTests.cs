using System.Diagnostics;

namespace OdinVS.Build.Tests;

// Real odin again: a chain RefApp -> RefLib -> RefBase of Source references (tests/samples/Ref*).
[TestClass]
[TestCategory("RequiresToolchain")]
public sealed class ReferenceTargetTests
{
    private static string Exe(string workDirectory, string project) => Path.Combine(workDirectory, project, "bin", "Debug", project + ".exe");

    private static MsBuildResult Build(string workDirectory, string project, string targets = "Build", params string[] properties) =>
        MsBuildSandbox.Instance.Build(Path.Combine(workDirectory, project), project + ".odinproj", targets, properties);

    private static void AppendToProject(string workDirectory, string project, string xml)
    {
        string file = Path.Combine(workDirectory, project, project + ".odinproj");
        File.WriteAllText(file, File.ReadAllText(file).Replace("</Project>", xml + "\n</Project>"));
    }

    [TestMethod]
    public void TransitiveSourceReferencesBuildAndRun()
    {
        string work = MsBuildSandbox.Instance.CopySamples("RefApp", "RefLib", "RefBase");
        MsBuildResult result = Build(work, "RefApp");
        Assert.AreEqual(0, result.ExitCode, result.Output);
        StringAssert.Contains(result.Output, "-collection:RefLib=" + Path.Combine(work, "RefLib"));
        StringAssert.Contains(result.Output, "-collection:RefBase=" + Path.Combine(work, "RefBase"));

        using Process run = Process.Start(new ProcessStartInfo(Exe(work, "RefApp")) { RedirectStandardOutput = true, UseShellExecute = false })!;
        string stdout = run.StandardOutput.ReadToEnd();
        run.WaitForExit();
        StringAssert.Contains(stdout, "RefApp total: 10");
    }

    [TestMethod]
    public void LibrarySourceChangeRebuildsDependentsAndAnUnchangedGraphIsSkipped()
    {
        string work = MsBuildSandbox.Instance.CopySamples("RefApp", "RefLib", "RefBase");
        MsBuildResult first = Build(work, "RefApp");
        Assert.AreEqual(0, first.ExitCode, first.Output);
        StringAssert.Contains(first.Output, "ODIN: ");

        MsBuildResult second = Build(work, "RefApp");
        Assert.AreEqual(0, second.ExitCode, second.Output);
        Assert.DoesNotContain("ODIN: ", second.Output, "an unchanged graph must not run odin");

        // Two levels down: RefApp's own files are untouched, only a transitively referenced source changes.
        File.SetLastWriteTimeUtc(Path.Combine(work, "RefBase", "src", "basemath", "basemath.odin"), DateTime.UtcNow.AddMinutes(1));
        MsBuildResult third = Build(work, "RefApp");
        Assert.AreEqual(0, third.ExitCode, third.Output);
        StringAssert.Contains(third.Output, "ODIN: ");
    }

    [TestMethod]
    public void CircularReferenceFailsTheBuildNamingTheProjects()
    {
        string work = MsBuildSandbox.Instance.CopySamples("RefCycleA", "RefCycleB");
        MsBuildResult result = Build(work, "RefCycleA");
        Assert.AreNotEqual(0, result.ExitCode, result.Output);
        StringAssert.Contains(result.Output, "ODIN0102");
        StringAssert.Contains(result.Output, "RefCycleA -> RefCycleB -> RefCycleA");
        Assert.DoesNotContain("MSB4006", result.Output, "the graph walk must not recurse through MSBuild");
        Assert.IsFalse(File.Exists(Exe(work, "RefCycleA")));
    }

    [TestMethod]
    public void TwoReferencedProjectsWithTheSameCollectionNameFail()
    {
        string work = MsBuildSandbox.Instance.CopySamples("RefApp", "RefLib", "RefBase");
        AppendToProject(work, "RefLib", "<PropertyGroup><OdinCollectionName>RefBase</OdinCollectionName></PropertyGroup>");
        MsBuildResult result = Build(work, "RefApp");
        Assert.AreNotEqual(0, result.ExitCode, result.Output);
        StringAssert.Contains(result.Output, "ODIN0103");
        StringAssert.Contains(result.Output, "'RefBase'");
    }

    [TestMethod]
    public void CollectionNameAndRootCanBeOverridden()
    {
        string work = MsBuildSandbox.Instance.CopySamples("RefApp", "RefLib", "RefBase");
        AppendToProject(work, "RefBase", "<PropertyGroup><OdinCollectionName>basecoll</OdinCollectionName><OdinCollectionRoot>src</OdinCollectionRoot></PropertyGroup>");
        // The importers use the new name and the new root (src instead of the project directory).
        File.WriteAllText(Path.Combine(work, "RefLib", "src", "greeter", "greeter.odin"), File.ReadAllText(Path.Combine(work, "RefLib", "src", "greeter", "greeter.odin")).Replace("RefBase:src/basemath", "basecoll:basemath"));
        MsBuildResult result = Build(work, "RefApp");
        Assert.AreEqual(0, result.ExitCode, result.Output);
        StringAssert.Contains(result.Output, "-collection:basecoll=" + Path.Combine(work, "RefBase", "src"));
    }
}
