using OdinVS.Core;

namespace OdinVS.Core.Tests;

[TestClass]
public sealed class OdinSolutionCollectionsTests
{
    private static string Project(string folder, string name, string body = "")
    {
        string directory = Path.Combine(folder, name);
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, name + ".odinproj");
        File.WriteAllText(file, $"<Project Sdk=\"Odin.Sdk/0.5.0-local\"><PropertyGroup>{body}</PropertyGroup></Project>");
        return file;
    }

    private static string NewFolder() => Directory.CreateTempSubdirectory("odinvs-sc").FullName;

    [TestMethod]
    public void EveryProjectGetsACollectionNamedAfterItAtItsFolder()
    {
        string folder = NewFolder();
        string a = Project(folder, "A");
        string b = Project(folder, "B");
        OdinSolutionCollectionsResult result = OdinSolutionCollections.Compute([b, a]);
        CollectionAssert.AreEqual(
            new[] { "A", "B" },
            result.Collections.Select(c => c.Key).ToArray());
        Assert.AreEqual(Path.Combine(folder, "A"), result.Collections[0].Value);
        Assert.IsEmpty(result.Problems);
    }

    [TestMethod]
    public void NameAndRootOverridesAreRead()
    {
        string folder = NewFolder();
        string a = Project(folder, "A", "<OdinCollectionName>core</OdinCollectionName><OdinCollectionRoot>src</OdinCollectionRoot>");
        OdinSolutionCollectionsResult result = OdinSolutionCollections.Compute([a]);
        Assert.AreEqual("core", result.Collections.Single().Key);
        Assert.AreEqual(Path.Combine(folder, "A", "src"), result.Collections.Single().Value);
    }

    [TestMethod]
    public void TwoProjectsWithTheSameNameAreReportedAndTheFirstWins()
    {
        string folder = NewFolder();
        string a = Project(folder, "A", "<OdinCollectionName>dup</OdinCollectionName>");
        string b = Project(folder, "B", "<OdinCollectionName>dup</OdinCollectionName>");
        OdinSolutionCollectionsResult result = OdinSolutionCollections.Compute([a, b]);
        Assert.AreEqual("dup", result.Collections.Single().Key);
        Assert.AreEqual(OdinSolutionCollections.DuplicateCode, result.Problems.Single().Code);
        StringAssert.Contains(result.Problems.Single().Message, "dup");
    }

    [TestMethod]
    public void AnUnevaluableValueFallsBackToTheDefaultAndIsReported()
    {
        string folder = NewFolder();
        string a = Project(folder, "A", "<OdinCollectionName>$(Something)</OdinCollectionName>");
        OdinSolutionCollectionsResult result = OdinSolutionCollections.Compute([a]);
        Assert.AreEqual("A", result.Collections.Single().Key);
        Assert.AreEqual(OdinSolutionCollections.UnreadableCode, result.Problems.Single().Code);
    }

    [TestMethod]
    public void AnUnreadableProjectFileFallsBackToTheDefaultAndIsReported()
    {
        string folder = NewFolder();
        string a = Project(folder, "A");
        File.WriteAllText(a, "<Project");
        OdinSolutionCollectionsResult result = OdinSolutionCollections.Compute([a]);
        Assert.AreEqual("A", result.Collections.Single().Key);
        Assert.AreEqual(OdinSolutionCollections.UnreadableCode, result.Problems.Single().Code);
    }

    [TestMethod]
    public void ProjectNameMacrosAreSubstituted()
    {
        string folder = NewFolder();
        string a = Project(folder, "A", "<OdinCollectionName>$(MSBuildProjectName)_x</OdinCollectionName>");
        Assert.AreEqual("A_x", OdinSolutionCollections.Compute([a]).Collections.Single().Key);
    }
}
