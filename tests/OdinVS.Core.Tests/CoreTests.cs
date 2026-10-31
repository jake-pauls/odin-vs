using OdinVS.Core;

namespace OdinVS.Core.Tests;

[TestClass]
public sealed class ToolLocatorTests
{
    [TestMethod]
    public void FindsFirstSearchDirectoryHit()
    {
        ToolLocator locator = new(p => p == Path.Combine("b", "odin.exe"));
        Assert.AreEqual(Path.Combine("b", "odin.exe"), locator.Find("odin.exe", ["a", "b"], ["fallback"]));
    }

    [TestMethod]
    public void FallsBackWhenNotInSearchDirectories()
    {
        ToolLocator locator = new(p => p == "fallback");
        Assert.AreEqual("fallback", locator.Find("odin.exe", ["a", ""], ["fallback"]));
    }

    [TestMethod]
    public void ReturnsNullWhenMissing()
    {
        Assert.IsNull(new ToolLocator(_ => false).Find("odin.exe", ["a"], ["f"]));
    }

    [TestMethod]
    public void TrimsQuotedPathEntries()
    {
        ToolLocator locator = new(p => p == Path.Combine("C:\\x y", "odin.exe"));
        Assert.IsNotNull(locator.Find("odin.exe", ["\"C:\\x y\""], []));
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

[TestClass]
public sealed class OdinPackageNameTests
{
    [TestMethod]
    [DataRow("package main\n\nimport \"core:fmt\"\n", "main")]
    [DataRow("package greeter\r\n", "greeter")]
    [DataRow("// header\n/* block\npackage fake\n*/\n#+build windows\npackage real_pkg\n", "real_pkg")]
    [DataRow("\tpackage tabbed", "tabbed")]
    public void FindsDeclaration(string source, string expected)
    {
        Assert.AreEqual(expected, OdinPackageName.Find(source));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("main :: proc() {}\n")]
    [DataRow("// package commented\n")]
    public void ReturnsNullWithoutDeclaration(string? source)
    {
        Assert.IsNull(OdinPackageName.Find(source));
    }
}
