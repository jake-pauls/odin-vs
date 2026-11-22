using System.Reflection;
using System.Text.RegularExpressions;

namespace OdinVS.Core.Tests;

// Keeps the diagnostic code table in docs/user-guide.md, OdinDiagnosticCodes and the literal in Sdk.targets from drifting apart.
[TestClass]
public sealed class DiagnosticCodesTests
{
    private static string[] CodesInClass()
    {
        return typeof(OdinDiagnosticCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();
    }

    [TestMethod]
    public void EveryCodeIsUnique()
    {
        string[] codes = CodesInClass();
        Assert.AreEqual(codes.Length, codes.Distinct().Count(), "two constants share a code");
    }

    [TestMethod]
    public void TheUserGuideTableListsExactlyTheCodesInTheClass()
    {
        string guide = File.ReadAllText(Path.Combine(RepoPaths.Root, "docs", "user-guide.md"));
        string[] documented = Regex.Matches(guide, @"^\| `(ODIN\d{4})` \|", RegexOptions.Multiline)
            .Cast<Match>()
            .Select(match => match.Groups[1].Value)
            .ToArray();
        CollectionAssert.AreEquivalent(CodesInClass(), documented, "docs/user-guide.md diagnostic table and OdinDiagnosticCodes differ");
    }

    [TestMethod]
    public void EveryCodeTheSdkTargetsRaiseIsDefinedInTheClass()
    {
        string targets = File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "OdinVS.Build", "Sdk", "Sdk.targets"));
        string[] raised = Regex.Matches(targets, @"Code=""(ODIN\d{4})""")
            .Cast<Match>()
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToArray();
        Assert.IsNotEmpty(raised, "Sdk.targets should raise at least ODIN0106");
        foreach (string code in raised)
        {
            Assert.Contains(code, CodesInClass(), $"Sdk.targets raises {code}, which OdinDiagnosticCodes does not define");
        }
    }
}
