using OdinVS.Core;

namespace OdinVS.Core.Tests;

[TestClass]
public sealed class ToolLocatorTests
{
    [TestMethod]
    public void FindsFirstSearchDirectoryHit()
    {
        ToolLocator locator = new(p => p == Path.Combine("C:\\b", "odin.exe"));
        Assert.AreEqual(Path.Combine("C:\\b", "odin.exe"), locator.Find("odin.exe", ["C:\\a", "C:\\b"], ["fallback"]));
    }

    [TestMethod]
    public void FallsBackWhenNotInSearchDirectories()
    {
        ToolLocator locator = new(p => p == "fallback");
        Assert.AreEqual("fallback", locator.Find("odin.exe", ["C:\\a", ""], ["fallback"]));
    }

    [TestMethod]
    public void ReturnsNullWhenMissing()
    {
        Assert.IsNull(new ToolLocator(_ => false).Find("odin.exe", ["C:\\a"], ["f"]));
    }

    [TestMethod]
    [DataRow(".")]
    [DataRow("bin")]
    [DataRow("\"\"")]
    [DataRow("  ")]
    public void SkipsRelativeAndEmptyEntries(string entry)
    {
        ToolLocator locator = new(_ => true);
        Assert.IsNull(locator.Find("odin.exe", [entry], []));
    }

    [TestMethod]
    public void TrimsQuotedPathEntries()
    {
        ToolLocator locator = new(p => p == Path.Combine("C:\\x y", "odin.exe"));
        Assert.IsNotNull(locator.Find("odin.exe", ["\"C:\\x y\""], []));
    }
}

[TestClass]
public sealed class ToolResolverTests
{
    private const string Extension = "C:\\ext";
    private static readonly string Ols = Path.Combine(Extension, "ols", "ols.exe");
    private static readonly string OdinOnPath = Path.Combine("C:\\tools", "odin.exe");

    [TestMethod]
    public void ResolvesBothTools()
    {
        ToolResolver resolver = new(new ToolLocator(p => p == Ols || p == OdinOnPath));
        ResolvedTools tools = resolver.Resolve(Extension, "C:\\other;C:\\tools");
        Assert.AreEqual(Ols, tools.Ols);
        Assert.AreEqual(OdinOnPath, tools.Odin);
        Assert.IsEmpty(tools.Problems);
    }

    [TestMethod]
    public void FallsBackToTheDefaultOdinLocation()
    {
        ToolResolver resolver = new(new ToolLocator(p => p == Ols || p == ToolResolver.OdinFallback));
        Assert.AreEqual(ToolResolver.OdinFallback, resolver.Resolve(Extension, null).Odin);
    }

    [TestMethod]
    public void ReportsMissingOdin()
    {
        ToolResolver resolver = new(new ToolLocator(p => p == Ols));
        ResolvedTools tools = resolver.Resolve(Extension, "C:\\tools");
        Assert.IsNull(tools.Odin);
        Assert.HasCount(1, tools.Problems);
        StringAssert.Contains(tools.Problems[0], "odin.exe not found");
    }

    [TestMethod]
    public void ReportsMissingOls()
    {
        ToolResolver resolver = new(new ToolLocator(p => p == OdinOnPath));
        ResolvedTools tools = resolver.Resolve(Extension, "C:\\tools");
        Assert.IsNull(tools.Ols);
        Assert.HasCount(1, tools.Problems);
        StringAssert.Contains(tools.Problems[0], "ols.exe not found");
    }

    [TestMethod]
    public void ReportsBothWhenNothingIsInstalled()
    {
        ResolvedTools tools = new ToolResolver(new ToolLocator(_ => false)).Resolve(Extension, "C:\\tools");
        Assert.HasCount(2, tools.Problems);
    }
}

[TestClass]
public sealed class VersionTests
{
    [TestMethod]
    [DataRow("dev-2026-10", 2026, 10)]
    [DataRow("dev-2026-10-nightly:84bc3fc", 2026, 10)]
    [DataRow("Starting Odin Language Server dev-2026-10", 2026, 10)]
    public void ParsesKnownShapes(string text, int year, int month)
    {
        Assert.AreEqual(new OdinVersion(year, month), OdinVersion.Parse(text));
    }

    [TestMethod]
    public void ParseReturnsNullForGarbage()
    {
        Assert.IsNull(OdinVersion.Parse("nonsense"));
        Assert.IsNull(OdinVersion.Parse(null));
    }

    [TestMethod]
    public void MatchingVersionsGiveNoWarning()
    {
        Assert.IsNull(VersionCompatibility.Check("dev-2026-10-nightly:84bc3fc", new OdinVersion(2026, 10)));
    }

    [TestMethod]
    public void MismatchedVersionsWarn()
    {
        string? warning = VersionCompatibility.Check("dev-2026-10-nightly:84bc3fc", new OdinVersion(2026, 7));
        Assert.IsNotNull(warning);
        StringAssert.Contains(warning, "dev-2026-07");
        StringAssert.Contains(warning, "dev-2026-10");
    }

    [TestMethod]
    public void UnknownVersionWarnsInsteadOfPassingSilently()
    {
        Assert.IsNotNull(VersionCompatibility.Check(null, new OdinVersion(2026, 10)));
    }
}

[TestClass]
public sealed class OlsConfigTests
{
    [TestMethod]
    public void EscapesBackslashesAndQuotes()
    {
        string json = OlsConfig.Generate(new OlsSettings("C:\\Program Files\\Odin\\odin.exe", ["C:\\a\"b"], []));
        StringAssert.Contains(json, "\"odin_command\": \"C:\\\\Program Files\\\\Odin\\\\odin.exe\"");
        StringAssert.Contains(json, "\"checker_path\": [\"C:\\\\a\\\"b\"]");
        StringAssert.Contains(json, "\"collections\": []");
    }

    [TestMethod]
    public void EmitsCollections()
    {
        string json = OlsConfig.Generate(new OlsSettings("odin", ["."], [new("hellolib", "C:\\libs\\hellolib")]));
        StringAssert.Contains(json, "{ \"name\": \"hellolib\", \"path\": \"C:\\\\libs\\\\hellolib\" }");
    }
}

[TestClass]
public sealed class OlsDiagnosticCodeTests
{
    [TestMethod]
    [DataRow("checker", "ols.checker")]
    [DataRow("syntax_error", "ols.syntax_error")]
    [DataRow("ols.checker", "ols.checker")]
    [DataRow("ols", "ols")]
    [DataRow(" checker ", "ols.checker")]
    [DataRow("42", "ols.42")]
    public void PrefixesOlsCodes(string code, string expected)
    {
        Assert.AreEqual(expected, OlsDiagnosticCode.Rewrite(code));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("  ")]
    public void MissingCodeBecomesPlainOls(string? code)
    {
        Assert.AreEqual("ols", OlsDiagnosticCode.Rewrite(code));
    }
}
