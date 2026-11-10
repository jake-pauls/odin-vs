namespace OdinVS.Core.Tests;

[TestClass]
public sealed class OdinWatchExpressionsTests
{
    [TestMethod]
    public void SingleIndexGetsDataAndABoundsCheck()
    {
        OdinWatchExpressions.IndexRewrite rewrite = OdinWatchExpressions.Rewrite("ints[99]", OdinWatchExpressions.FindBrackets("ints[99]", 4), 1);
        Assert.AreEqual("ints.data[99]", rewrite.Text);
        Assert.HasCount(1, rewrite.Checks);
        Assert.AreEqual("ints", rewrite.Checks[0].Base);
        Assert.AreEqual("99", rewrite.Checks[0].Index);
    }

    [TestMethod]
    public void NestedIndexBaseIncludesTheEarlierRewrite()
    {
        const string text = "things[0].tags[i + 1]";
        IReadOnlyList<int> brackets = OdinWatchExpressions.FindBrackets(text, 4);
        OdinWatchExpressions.IndexRewrite both = OdinWatchExpressions.Rewrite(text, brackets, 3);
        Assert.AreEqual("things.data[0].tags.data[i + 1]", both.Text);
        Assert.AreEqual("things", both.Checks[0].Base);
        Assert.AreEqual("0", both.Checks[0].Index);
        Assert.AreEqual("things.data[0].tags", both.Checks[1].Base);
        Assert.AreEqual("i + 1", both.Checks[1].Index);

        OdinWatchExpressions.IndexRewrite second = OdinWatchExpressions.Rewrite(text, brackets, 2);
        Assert.AreEqual("things[0].tags.data[i + 1]", second.Text);
        Assert.HasCount(1, second.Checks);
        Assert.AreEqual("things[0].tags", second.Checks[0].Base);
    }

    [TestMethod]
    public void IndexInsideAnIndexIsRewrittenAndCheckedToo()
    {
        const string text = "a[b[1]]";
        OdinWatchExpressions.IndexRewrite both = OdinWatchExpressions.Rewrite(text, OdinWatchExpressions.FindBrackets(text, 4), 3);
        Assert.AreEqual("a.data[b.data[1]]", both.Text);
        Assert.AreEqual("b.data[1]", both.Checks[0].Index);
        Assert.AreEqual("1", both.Checks[1].Index);
    }

    [TestMethod]
    public void CastedBaseIsKeptWhole()
    {
        const string text = "(*p)[2]";
        OdinWatchExpressions.IndexRewrite rewrite = OdinWatchExpressions.Rewrite(text, OdinWatchExpressions.FindBrackets(text, 4), 1);
        Assert.AreEqual("(*p).data[2]", rewrite.Text);
        Assert.AreEqual("(*p)", rewrite.Checks[0].Base);
    }

    [TestMethod]
    public void MapLookupIsSplitIntoBaseIndexAndRest()
    {
        OdinWatchExpressions.MapLookupShape? shape = OdinWatchExpressions.SplitLookup("name_set[\"red\"]");
        Assert.IsNotNull(shape);
        Assert.AreEqual("name_set", shape.Base);
        Assert.AreEqual("\"red\"", shape.Index);
        Assert.AreEqual(string.Empty, shape.Rest);

        shape = OdinWatchExpressions.SplitLookup("holder.things[k].name");
        Assert.IsNotNull(shape);
        Assert.AreEqual("holder.things", shape.Base);
        Assert.AreEqual("k", shape.Index);
        Assert.AreEqual(".name", shape.Rest);
    }

    [TestMethod]
    public void NonLookupShapesAreNotSplit()
    {
        Assert.IsNull(OdinWatchExpressions.SplitLookup("ints"));
        Assert.IsNull(OdinWatchExpressions.SplitLookup("[1]"));
        Assert.IsNull(OdinWatchExpressions.SplitLookup("f(x)[1]"));
        Assert.IsNull(OdinWatchExpressions.SplitLookup("a[]"));
        Assert.IsNull(OdinWatchExpressions.SplitLookup("a[1"));
    }

    [TestMethod]
    public void StringLiteralsAreParsedIncludingEscapes()
    {
        Assert.IsTrue(OdinWatchExpressions.TryParseStringLiteral("\"red\"", out byte[] red));
        CollectionAssert.AreEqual("red"u8.ToArray(), red);
        Assert.IsTrue(OdinWatchExpressions.TryParseStringLiteral(" \"a\\\"b\\n\" ", out byte[] escaped));
        CollectionAssert.AreEqual("a\"b\n"u8.ToArray(), escaped);
        Assert.IsTrue(OdinWatchExpressions.TryParseStringLiteral("\"\"", out byte[] empty));
        Assert.IsEmpty(empty);
        Assert.IsTrue(OdinWatchExpressions.TryParseStringLiteral("\"é\"", out byte[] accent));
        CollectionAssert.AreEqual(new byte[] { 0xC3, 0xA9 }, accent);
    }

    [TestMethod]
    public void NonLiteralsAreNotParsedAsStrings()
    {
        Assert.IsFalse(OdinWatchExpressions.TryParseStringLiteral("name", out _));
        Assert.IsFalse(OdinWatchExpressions.TryParseStringLiteral("\"a\" + \"b\"", out _));
        Assert.IsFalse(OdinWatchExpressions.TryParseStringLiteral("\"a\\q\"", out _));
        Assert.IsFalse(OdinWatchExpressions.TryParseStringLiteral("\"", out _));
    }

    [TestMethod]
    public void NonBmpCharactersInAStringLiteralAreEncodedWhole()
    {
        Assert.IsTrue(OdinWatchExpressions.TryParseStringLiteral("\"a😀\"", out byte[] bytes));
        CollectionAssert.AreEqual("a😀"u8.ToArray(), bytes);
    }

    [TestMethod]
    public void FindBracketsStopsAtTheMaximum()
    {
        CollectionAssert.AreEqual(new[] { 1, 4 }, OdinWatchExpressions.FindBrackets("a[1][2][3]", 2).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 4, 7 }, OdinWatchExpressions.FindBrackets("a[1][2][3]", 9).ToArray());
        Assert.IsEmpty(OdinWatchExpressions.FindBrackets("plain", 4));
    }

    [TestMethod]
    public void EmptyMaskLeavesTheTextAndHasNoChecks()
    {
        OdinWatchExpressions.IndexRewrite rewrite = OdinWatchExpressions.Rewrite("a[1]", OdinWatchExpressions.FindBrackets("a[1]", 4), 0);
        Assert.AreEqual("a[1]", rewrite.Text);
        Assert.IsEmpty(rewrite.Checks);
    }

    [TestMethod]
    public void SplitLookupTakesTheFirstBracketPairAndKeepsTheRest()
    {
        OdinWatchExpressions.MapLookupShape? shape = OdinWatchExpressions.SplitLookup("a[1][2]");
        Assert.IsNotNull(shape);
        Assert.AreEqual("1", shape.Index);
        Assert.AreEqual("[2]", shape.Rest);

        shape = OdinWatchExpressions.SplitLookup("a[b[1]]");
        Assert.IsNotNull(shape);
        Assert.AreEqual("b[1]", shape.Index);
        Assert.AreEqual(string.Empty, shape.Rest);
    }

    [TestMethod]
    public void SplitLookupIgnoresBracketsInsideAStringLiteral()
    {
        OdinWatchExpressions.MapLookupShape? shape = OdinWatchExpressions.SplitLookup("m[\"a]b\"]");
        Assert.IsNotNull(shape);
        Assert.AreEqual("\"a]b\"", shape.Index);
        Assert.AreEqual(string.Empty, shape.Rest);

        shape = OdinWatchExpressions.SplitLookup("m[\"q\\\"]\"].x");
        Assert.IsNotNull(shape);
        Assert.AreEqual("\"q\\\"]\"", shape.Index);
        Assert.AreEqual(".x", shape.Rest);
    }

    [TestMethod]
    public void SplitLookupAcceptsQualifiedNames()
    {
        Assert.AreEqual("pkg::g", OdinWatchExpressions.SplitLookup("pkg::g[1]")!.Base);
        Assert.IsNull(OdinWatchExpressions.SplitLookup("a->b[1]"), "arrow members are not a plain member chain (not supported yet)");
    }

    [TestMethod]
    public void LittleEndianKeepsTheLowBytes()
    {
        CollectionAssert.AreEqual(new byte[] { 5, 0, 0, 0 }, OdinWatchExpressions.LittleEndian(5, 4));
        CollectionAssert.AreEqual(new byte[] { 0xFF, 0xFF }, OdinWatchExpressions.LittleEndian(-1, 2));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 0, 0, 0, 0, 0, 0 }, OdinWatchExpressions.LittleEndian(0x0201, 8));
        CollectionAssert.AreEqual(new byte[] { 44 }, OdinWatchExpressions.LittleEndian(300, 1), "no range check here: callers must reject a value that does not fit the key");
    }
}
