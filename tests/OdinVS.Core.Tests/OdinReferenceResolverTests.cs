using OdinVS.Core;

namespace OdinVS.Core.Tests;

[TestClass]
public sealed class OdinReferenceResolverTests
{
    private static string PathOf(string name) => $@"C:\work\{name}\{name}.odinproj";

    private static OdinProjectNode Odin(string name, params (string Target, OdinReferenceKind Kind)[] references) =>
        new(
            PathOf(name),
            name,
            name.ToLowerInvariant(),
            $@"C:\work\{name}",
            string.Empty,
            false,
            references.Select(r => new OdinReferenceEdge(r.Target.Contains('.') ? r.Target : PathOf(r.Target), r.Kind)).ToList(),
            [$@"C:\work\{name}\src\{name}.odin"]);

    private static OdinProjectNode Native(string name) =>
        new($@"C:\work\{name}\{name}.vcxproj", name, name, $@"C:\work\{name}", $@"C:\work\{name}\x64\Debug\{name}.lib", true, [], []);

    private static (string, OdinReferenceKind) Src(string name) => (name, OdinReferenceKind.Source);

    private static (string, OdinReferenceKind) Link(string name) => (name, OdinReferenceKind.Link);

    [TestMethod]
    public void ProjectWithoutReferencesNeedsNothing()
    {
        OdinResolvedReferences result = OdinReferenceResolver.Resolve(PathOf("App"), [Odin("App")]);

        Assert.IsEmpty(result.Problems);
        Assert.IsEmpty(result.Collections);
        Assert.IsEmpty(result.LinkInputs);
        Assert.IsEmpty(result.Sources);
    }

    [TestMethod]
    public void SourceReferencesAreTransitive()
    {
        OdinResolvedReferences result = OdinReferenceResolver.Resolve(
            PathOf("App"),
            [Odin("App", Src("Mid")), Odin("Mid", Src("Base")), Odin("Base")]);

        Assert.IsEmpty(result.Problems);
        CollectionAssert.AreEqual(new[] { "mid", "base" }, result.Collections.Select(c => c.Key).ToArray());
        Assert.AreEqual(@"C:\work\Base", result.Collections[1].Value);
        CollectionAssert.AreEqual(new[] { @"C:\work\Mid\src\Mid.odin", @"C:\work\Base\src\Base.odin" }, result.Sources.ToArray());
    }

    [TestMethod]
    public void DiamondContributesEachCollectionAndSourceOnce()
    {
        OdinResolvedReferences result = OdinReferenceResolver.Resolve(
            PathOf("App"),
            [Odin("App", Src("Left"), Src("Right")), Odin("Left", Src("Base")), Odin("Right", Src("Base")), Odin("Base")]);

        Assert.IsEmpty(result.Problems);
        CollectionAssert.AreEqual(new[] { "left", "base", "right" }, result.Collections.Select(c => c.Key).ToArray());
        Assert.HasCount(3, result.Sources);
    }

    [TestMethod]
    public void LinkReferenceToNativeProjectAddsItsLibraryAndNoCollection()
    {
        OdinResolvedReferences result = OdinReferenceResolver.Resolve(
            PathOf("App"),
            [Odin("App", Link("Native")) with { References = [new OdinReferenceEdge(Native("Native").ProjectPath, OdinReferenceKind.Link)] }, Native("Native")]);

        Assert.IsEmpty(result.Problems);
        Assert.IsEmpty(result.Collections);
        CollectionAssert.AreEqual(new[] { @"C:\work\Native\x64\Debug\Native.lib" }, result.LinkInputs.ToArray());
    }

    [TestMethod]
    public void NativeLibraryBehindASourceReferenceReachesTheFinalLink()
    {
        OdinProjectNode lib = Odin("Lib") with { References = [new OdinReferenceEdge(Native("Native").ProjectPath, OdinReferenceKind.Link)] };
        OdinResolvedReferences result = OdinReferenceResolver.Resolve(PathOf("App"), [Odin("App", Src("Lib")), lib, Native("Native")]);

        Assert.IsEmpty(result.Problems);
        CollectionAssert.AreEqual(new[] { "lib" }, result.Collections.Select(c => c.Key).ToArray());
        Assert.HasCount(1, result.LinkInputs);
    }

    [TestMethod]
    public void SourceReferenceToANativeProjectIsTreatedAsALink()
    {
        OdinProjectNode app = Odin("App") with { References = [new OdinReferenceEdge(Native("Native").ProjectPath, OdinReferenceKind.Source)] };
        OdinResolvedReferences result = OdinReferenceResolver.Resolve(PathOf("App"), [app, Native("Native")]);

        Assert.IsEmpty(result.Problems);
        Assert.IsEmpty(result.Collections);
        Assert.HasCount(1, result.LinkInputs);
    }

    [TestMethod]
    public void LinkingAProjectWithoutALibraryIsAnError()
    {
        OdinResolvedReferences result = OdinReferenceResolver.Resolve(PathOf("App"), [Odin("App", Link("Lib")), Odin("Lib")]);

        Assert.HasCount(1, result.Problems);
        Assert.AreEqual(OdinReferenceResolver.NoLibraryCode, result.Problems[0].Code);
        StringAssert.Contains(result.Problems[0].Message, "'Lib' produces no library");
    }

    [TestMethod]
    public void TwoLinkedLibrariesWithOneFileNameAreAnError()
    {
        OdinProjectNode a = new(@"C:\work\A\A.vcxproj", "A", "A", @"C:\work\A", @"C:\work\A\out\x.lib", true, [], []);
        OdinProjectNode b = new(@"C:\work\B\B.vcxproj", "B", "B", @"C:\work\B", @"C:\work\B\out\X.LIB", true, [], []);
        OdinResolvedReferences result = OdinReferenceResolver.Resolve(PathOf("App"), [Odin("App", Link(a.ProjectPath), Link(b.ProjectPath)), a, b]);

        Assert.HasCount(1, result.Problems);
        Assert.AreEqual(OdinReferenceResolver.DuplicateLibraryCode, result.Problems[0].Code);
        StringAssert.Contains(result.Problems[0].Message, "TargetName");
    }

    [TestMethod]
    public void TwoLinkedLibrariesWithDifferentFileNamesAreFine()
    {
        OdinResolvedReferences result = OdinReferenceResolver.Resolve(PathOf("App"), [Odin("App", Link(Native("A").ProjectPath), Link(Native("B").ProjectPath)), Native("A"), Native("B")]);

        Assert.IsEmpty(result.Problems);
        Assert.HasCount(2, result.LinkInputs);
    }

    [TestMethod]
    public void CycleIsRejectedWithItsPath()
    {
        OdinResolvedReferences result = OdinReferenceResolver.Resolve(
            PathOf("A"),
            [Odin("A", Src("B")), Odin("B", Src("C")), Odin("C", Src("A"))]);

        Assert.HasCount(1, result.Problems);
        Assert.AreEqual(OdinReferenceResolver.CycleCode, result.Problems[0].Code);
        StringAssert.Contains(result.Problems[0].Message, "A -> B -> C -> A");
    }

    [TestMethod]
    public void CycleNotInvolvingTheRootShowsOnlyTheCycle()
    {
        OdinResolvedReferences result = OdinReferenceResolver.Resolve(
            PathOf("App"),
            [Odin("App", Src("B")), Odin("B", Src("C")), Odin("C", Src("B"))]);

        Assert.HasCount(1, result.Problems);
        StringAssert.Contains(result.Problems[0].Message, "B -> C -> B");
        Assert.DoesNotContain("App ->", result.Problems[0].Message);
    }

    [TestMethod]
    public void SelfReferenceIsACycle()
    {
        OdinResolvedReferences result = OdinReferenceResolver.Resolve(PathOf("A"), [Odin("A", Src("A"))]);

        Assert.HasCount(1, result.Problems);
        StringAssert.Contains(result.Problems[0].Message, "A -> A");
    }

    [TestMethod]
    public void LinkEdgesCountForCycles()
    {
        OdinResolvedReferences result = OdinReferenceResolver.Resolve(PathOf("A"), [Odin("A", Link("B")), Odin("B", Src("A"))]);

        Assert.AreEqual(OdinReferenceResolver.CycleCode, result.Problems.Single().Code);
    }

    [TestMethod]
    public void TwoProjectsWithTheSameCollectionNameAreRejected()
    {
        OdinProjectNode other = Odin("Other") with { CollectionName = "left" };
        OdinResolvedReferences result = OdinReferenceResolver.Resolve(
            PathOf("App"),
            [Odin("App", Src("Left"), Src("Other")), Odin("Left"), other]);

        Assert.HasCount(1, result.Problems);
        Assert.AreEqual(OdinReferenceResolver.DuplicateCollectionCode, result.Problems[0].Code);
        StringAssert.Contains(result.Problems[0].Message, "'left'");
    }

    [TestMethod]
    public void ReferenceToAnUnreadProjectIsReported()
    {
        OdinResolvedReferences result = OdinReferenceResolver.Resolve(PathOf("App"), [Odin("App", Src("Missing"))]);

        Assert.AreEqual(OdinReferenceResolver.UnresolvedReferenceCode, result.Problems.Single().Code);
    }

    [TestMethod]
    public void PathComparisonIgnoresCase()
    {
        OdinProjectNode app = Odin("App") with { References = [new OdinReferenceEdge(PathOf("Lib").ToUpperInvariant(), OdinReferenceKind.Source)] };
        OdinResolvedReferences result = OdinReferenceResolver.Resolve(PathOf("App"), [app, Odin("Lib")]);

        Assert.IsEmpty(result.Problems);
        Assert.HasCount(1, result.Collections);
    }
}
