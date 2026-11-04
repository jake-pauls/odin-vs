using OdinVS.Core;

namespace OdinVS.Core.Tests;

[TestClass]
public sealed class ToolWarningsTests
{
    private static readonly OdinVersion m_Bundled = new(2026, 10);

    [TestMethod]
    public void NoWarningsWhenToolsFoundAndVersionsMatch()
    {
        ResolvedTools tools = new("ols", "odin", []);
        Assert.IsEmpty(ToolWarnings.Compute(tools, "odin version dev-2026-10", m_Bundled));
    }

    [TestMethod]
    public void MissingToolsAreReportedAndVersionIsNotChecked()
    {
        ResolvedTools tools = new(null, null, ["ols missing", "odin missing"]);
        Assert.HasCount(2, ToolWarnings.Compute(tools, "dev-2020-01", m_Bundled));
    }

    [TestMethod]
    public void MismatchedVersionIsReported()
    {
        ResolvedTools tools = new("ols", "odin", []);
        IReadOnlyList<string> warnings = ToolWarnings.Compute(tools, "odin version dev-2026-09", m_Bundled);
        Assert.HasCount(1, warnings);
        Assert.Contains("dev-2026-09", warnings[0]);
    }
}
