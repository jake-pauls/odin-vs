using OdinVS.Core;

namespace OdinVS.Core.Tests;

[TestClass]
public sealed class OdinNumericLiteralTests
{
    [TestMethod]
    [DataRow("y := 1.5", 'f', true)]
    [DataRow("y := 1", 'x', true)]
    [DataRow("y := 0x", 'F', true)]
    [DataRow("y := 1_000", 'a', true)]
    [DataRow("y := 1.5", '5', true)]
    [DataRow("y := x", 'f', false)]
    [DataRow("fmt.", 'p', false)]
    [DataRow("fmt.println1", 'a', false)]
    [DataRow("a.b1", 'c', false)]
    [DataRow("", 'a', false)]
    [DataRow("y := 1.5", ' ', false)]
    [DataRow("y := 1.5", '(', false)]
    [DataRow("arr[0].", 'a', false)]
    public void ContinuesNumberOnlyInsideNumberTokens(string before, char typed, bool expected)
    {
        Assert.AreEqual(expected, OdinNumericLiteral.ContinuesNumber(before, typed));
    }
}