using OdinVS.Core;

namespace OdinVS.Core.Tests;

[TestClass]
public sealed class OdinErrorParserTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(RepoPaths.Root, "tests", "OdinVS.Core.Tests", "Fixtures", name));

    [TestMethod]
    public void ParsesRealOdinErrors()
    {
        OdinErrorOutput output = OdinErrorParser.Parse(Fixture("odin-errors.json"));
        Assert.HasCount(2, output.Diagnostics);
        OdinDiagnostic first = output.Diagnostics[0];
        Assert.IsFalse(first.IsWarning);
        Assert.AreEqual(@"D:\Projects\Vibe\odin-vs\tests\samples\BrokenOdin\src\main.odin", first.File);
        Assert.AreEqual(6, first.Line);
        Assert.AreEqual(11, first.Column);
        Assert.AreEqual(17, first.EndColumn);
        Assert.AreEqual("Cannot convert '\"text\"' to 'int' from 'untyped string', got \"text\"", first.Message);
        Assert.AreEqual("Undeclared name: y", output.Diagnostics[1].Message);
        Assert.AreEqual(string.Empty, output.OtherText);
    }

    [TestMethod]
    public void KeepsTextBeforeTheJson()
    {
        OdinErrorOutput output = OdinErrorParser.Parse("LINK : warning something\n" + Fixture("odin-errors.json"));
        Assert.HasCount(2, output.Diagnostics);
        Assert.AreEqual("LINK : warning something", output.OtherText);
    }

    [TestMethod]
    public void KeepsDiagnosticsWhenTextFollowsTheJson()
    {
        OdinErrorOutput output = OdinErrorParser.Parse(Fixture("odin-errors.json") + "\nLINK : trailing note\n");
        Assert.HasCount(2, output.Diagnostics);
        Assert.AreEqual("LINK : trailing note", output.OtherText);
    }

    [TestMethod]
    public void ReturnsNonJsonTextUnparsed()
    {
        OdinErrorOutput output = OdinErrorParser.Parse("link.exe failed\n");
        Assert.IsEmpty(output.Diagnostics);
        Assert.AreEqual("link.exe failed", output.OtherText);
    }

    [TestMethod]
    public void ClampsColumnZeroAndJoinsDetailLines()
    {
        const string Json = "{\"error_count\":1,\"errors\":[{\"type\":\"error\",\"pos\":{\"file\":\"C:/a/b.odin\",\"line\":11,\"column\":0,\"end_column\":0},\"msgs\":[\"first\",\"detail\"]}]}";
        OdinDiagnostic diagnostic = OdinErrorParser.Parse(Json).Diagnostics[0];
        Assert.AreEqual(1, diagnostic.Column);
        Assert.AreEqual("first detail", diagnostic.Message);
    }

    [TestMethod]
    public void ParsesRealOdinDeprecationWarning()
    {
        OdinErrorOutput output = OdinErrorParser.Parse(Fixture("odin-warning.json"));
        OdinDiagnostic warning = output.Diagnostics.Single();
        Assert.IsTrue(warning.IsWarning);
        Assert.AreEqual(11, warning.Line);
        Assert.AreEqual(2, warning.Column);
        Assert.AreEqual("old_thing is deprecated: use new_thing instead", warning.Message);
    }

    [TestMethod]
    public void WarningTypeIsAWarning()
    {
        const string Json = "{\"errors\":[{\"type\":\"warning\",\"pos\":{\"file\":\"a.odin\",\"line\":1,\"column\":1,\"end_column\":2},\"msgs\":[\"w\"]}]}";
        Assert.IsTrue(OdinErrorParser.Parse(Json).Diagnostics[0].IsWarning);
    }

    [TestMethod]
    public void TruncatedJsonIsNotParsed()
    {
        OdinErrorOutput output = OdinErrorParser.Parse("{\"errors\":[{\"type\":");
        Assert.IsEmpty(output.Diagnostics);
        Assert.AreNotEqual(string.Empty, output.OtherText);
    }
}

[TestClass]
public sealed class OdinBuildArgumentsTests
{
    private static OdinBuildOptions Options(bool debug, string extra = "", params KeyValuePair<string, string>[] collections)
    {
        return new OdinBuildOptions(@"C:\p\src", @"C:\p\bin\Debug\p.exe", debug, collections, [], extra);
    }

    [TestMethod]
    public void DebugAddsDebugFlag()
    {
        Assert.AreEqual(@"build C:\p\src -out:C:\p\bin\Debug\p.exe -debug -strict-style -json-errors", OdinBuildArguments.Build(Options(true)));
    }

    [TestMethod]
    public void ReleaseOmitsDebugFlag()
    {
        string arguments = OdinBuildArguments.Build(Options(false));
        Assert.DoesNotContain("-debug", arguments);
        Assert.Contains("-strict-style", arguments);
    }

    [TestMethod]
    public void QuotesPathsWithSpacesAndTrimsTrailingSlash()
    {
        OdinBuildOptions options = new(@"C:\my proj\src\", @"C:\my proj\out.exe", false, [], [], "");
        Assert.AreEqual("""build "C:\my proj\src" "-out:C:\my proj\out.exe" -strict-style -json-errors""", OdinBuildArguments.Build(options));
    }

    [TestMethod]
    public void AddsCollectionsAndExtraArguments()
    {
        string arguments = OdinBuildArguments.Build(Options(true, " -vet ", new KeyValuePair<string, string>("lib", @"C:\libs\lib\")));
        Assert.Contains(@"-collection:lib=C:\libs\lib ", arguments + " ");
        Assert.EndsWith("-json-errors -vet", arguments);
    }

    [TestMethod]
    public void LinkInputsGoToTheLinkerWithTheirFoldersAsLibPaths()
    {
        OdinBuildOptions options = new(@"C:\p\src", @"C:\p\p.exe", false, [], [@"C:\n\a.lib", @"C:\n\b.lib", @"C:\m\c.lib"], "");
        string arguments = OdinBuildArguments.Build(options);
        Assert.Contains("\"-extra-linker-flags:/LIBPATH:C:\\n /LIBPATH:C:\\m /DEFAULTLIB:a.lib /DEFAULTLIB:b.lib /DEFAULTLIB:c.lib\" ", arguments);
        Assert.IsLessThan(arguments.IndexOf("-json-errors", StringComparison.Ordinal), arguments.IndexOf("-extra-linker-flags", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ALinkInputWithSpacesIsPassedRelativeToTheWorkingDirectory()
    {
        OdinBuildOptions options = new(@"C:\my work\p\src", @"C:\my work\p\p.exe", false, [], [@"C:\my work\lib\bin\b.lib"], "", "Exe", @"C:\my work\p");
        string arguments = OdinBuildArguments.Build(options);
        Assert.Contains("\"-extra-linker-flags:/LIBPATH:..\\lib\\bin /DEFAULTLIB:b.lib\" ", arguments);
    }

    [TestMethod]
    public void ALinkInputWithSpacesCanStayWhitespacedAfterRelativizing()
    {
        Assert.IsTrue(OdinBuildArguments.HasWhitespace(OdinBuildArguments.ToLinkerPath(@"C:\a b\x.lib", @"D:\p")));
        Assert.IsTrue(OdinBuildArguments.HasWhitespace(OdinBuildArguments.ToLinkerPath(@"C:\a b\x.lib", @"C:\p")));
    }

    [TestMethod]
    [DataRow("Dll", "-build-mode:dll")]
    [DataRow("dll", "-build-mode:dll")]
    [DataRow("Lib", "-build-mode:lib")]
    public void OutputTypeSelectsTheBuildMode(string outputType, string flag)
    {
        OdinBuildOptions options = new(@"C:\p\src", @"C:\p\p.dll", false, [], [], "", outputType);
        Assert.Contains(" " + flag + " ", OdinBuildArguments.Build(options) + " ");
    }

    [TestMethod]
    public void AnExecutableAddsNoBuildMode()
    {
        Assert.DoesNotContain("-build-mode", OdinBuildArguments.Build(Options(false)));
    }

    [TestMethod]
    public void NoLinkInputsAddNoLinkerFlag()
    {
        Assert.DoesNotContain("-extra-linker-flags", OdinBuildArguments.Build(Options(false)));
    }
}
