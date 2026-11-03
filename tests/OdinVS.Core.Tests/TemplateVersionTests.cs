using System.Text.RegularExpressions;

namespace OdinVS.Core.Tests;

// Tier 1: every checked-in .odinproj that names Odin.Sdk uses the version that src/OdinVS.Build packs, so a version bump
// cannot leave the VS template (or a sample) restoring a package that no longer exists in nuget/.
[TestClass]
public sealed class TemplateVersionTests
{
    private static string PackedVersion()
    {
        string text = File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "OdinVS.Build", "Odin.Sdk.csproj"));
        Match match = Regex.Match(text, "<Version>([^<]+)</Version>");
        Assert.IsTrue(match.Success, "no <Version> in Odin.Sdk.csproj; update this test's pattern");
        return match.Groups[1].Value;
    }

    [TestMethod]
    public void ProjectTemplateUsesThePackedSdkVersion()
    {
        string template = File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "OdinVS", "Templates", "ProjectTemplates", "OdinExecutable", "OdinApp.odinproj"));
        Assert.Contains($"Sdk=\"Odin.Sdk/{PackedVersion()}\"", template);
    }

    [TestMethod]
    public void SampleProjectsUseThePackedSdkVersion()
    {
        string[] projects = Directory.GetFiles(Path.Combine(RepoPaths.Root, "tests", "samples"), "*.odinproj", SearchOption.AllDirectories);
        Assert.IsNotEmpty(projects);
        foreach (string project in projects)
        {
            if (project.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            Assert.Contains($"Odin.Sdk/{PackedVersion()}", File.ReadAllText(project), project);
        }
    }
}
