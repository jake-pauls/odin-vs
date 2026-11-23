using System.Text.RegularExpressions;

namespace OdinVS.Core.Tests;

// Tier 1: odin.pkgdef is hand-written, so the package and UI-context GUIDs it registers must stay equal to the constants in
// OdinVSPackage. A mismatch loads nothing and VS reports nothing (see docs/dev/pitfalls.md).
[TestClass]
public sealed class PkgdefGuidTests
{
    private static string Pkgdef => File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "OdinVS", "odin.pkgdef"));

    private static string PackageSource => File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "OdinVS.ProjectSystem", "OdinVSPackage.cs"));

    private static string Constant(string name)
    {
        Match match = Regex.Match(PackageSource, name + " = \"([0-9a-fA-F-]{36})\"");
        Assert.IsTrue(match.Success, $"constant {name} not found in OdinVSPackage.cs; update this test");
        return match.Groups[1].Value;
    }

    private static bool Has(string text) => Pkgdef.Contains(text, StringComparison.OrdinalIgnoreCase);

    [TestMethod]
    public void PackageGuidIsRegisteredAndAutoLoaded()
    {
        string package = Constant("PackageGuidString");
        Assert.IsTrue(Has(@"\Packages\{" + package + "}]"), "package GUID is not registered under Packages in odin.pkgdef");
        Assert.IsTrue(Has("\"{" + package + "}\"=dword:00000002"), "package GUID is not auto-loaded with the background flag");
    }

    [TestMethod]
    public void UiContextGuidHasARuleAndTriggersTheAutoLoad()
    {
        string context = Constant("OdinProjectContextGuidString");
        Assert.IsTrue(Has(@"\UIContextRules\{" + context + "}]"), "UI context rule is missing or under the wrong key (it is UIContextRules)");
        Assert.IsTrue(Has(@"\AutoLoadPackages\{" + context + "}]"), "no AutoLoadPackages entry for the UI context");
    }

    [TestMethod]
    public void PackageRegistrationUsesTheBackgroundLoadValueName()
    {
        Assert.IsTrue(Has("\"AllowsBackgroundLoad\"=dword:00000001"), "the value is AllowsBackgroundLoad, not AllowsBackgroundLoading");
    }
}
