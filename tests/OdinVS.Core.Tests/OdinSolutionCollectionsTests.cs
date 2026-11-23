using OdinVS.Core;

namespace OdinVS.Core.Tests;

[TestClass]
public sealed class OdinSolutionCollectionsTests
{
    private static OdinProjectCollection Project(string name, string collection, string root) =>
        new(@"C:\s\" + name + @"\" + name + ".odinproj", collection, root);

    [TestMethod]
    public void EveryProjectGetsItsCollectionSortedByProjectPath()
    {
        OdinSolutionCollectionsResult result = OdinSolutionCollections.Compute([Project("B", "B", @"C:\s\B"), Project("A", "A", @"C:\s\A\")]);
        CollectionAssert.AreEqual(new[] { "A", "B" }, result.Collections.Select(c => c.Key).ToArray());
        Assert.AreEqual(@"C:\s\A", result.Collections[0].Value, "a trailing slash is trimmed");
        Assert.IsEmpty(result.Problems);
    }

    [TestMethod]
    public void TwoProjectsWithTheSameNameAreReportedAndTheFirstWins()
    {
        OdinSolutionCollectionsResult result = OdinSolutionCollections.Compute([Project("B", "dup", @"C:\s\B"), Project("A", "dup", @"C:\s\A")]);
        Assert.AreEqual(@"C:\s\A", result.Collections.Single().Value);
        Assert.AreEqual(OdinDiagnosticCodes.DuplicateCollectionName, result.Problems.Single().Code);
        StringAssert.Contains(result.Problems.Single().Message, "dup");
        StringAssert.Contains(result.Problems.Single().Message, "Give each project a different OdinCollectionName");
    }

    [TestMethod]
    [DataRow("bad\"name", @"C:\s\A")]
    [DataRow("ok", "C:\\s\\A\" -foo")]
    [DataRow("", @"C:\s\A")]
    public void ACollectionWithAQuoteOrAnEmptyValueIsReportedAndNotGivenToTheLanguageServer(string name, string root)
    {
        OdinSolutionCollectionsResult result = OdinSolutionCollections.Compute([Project("A", name, root), Project("B", "fine", @"C:\s\B")]);
        Assert.AreEqual("fine", result.Collections.Single().Key);
        Assert.AreEqual(OdinDiagnosticCodes.InvalidCollection, result.Problems.Single().Code);
    }

    [TestMethod]
    public void TheSameNameAtTheSameRootIsNotACollision()
    {
        OdinSolutionCollectionsResult result = OdinSolutionCollections.Compute([Project("A", "x", @"C:\s\shared"), Project("B", "x", @"c:\S\shared\")]);
        Assert.HasCount(1, result.Collections);
        Assert.IsEmpty(result.Problems);
    }

    [TestMethod]
    public void NoProjectsGiveNoCollections()
    {
        OdinSolutionCollectionsResult result = OdinSolutionCollections.Compute([]);
        Assert.IsEmpty(result.Collections);
        Assert.IsEmpty(result.Problems);
    }
}
