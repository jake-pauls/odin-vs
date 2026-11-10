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
    public void LittleEndianKeepsTheLowBytes()
    {
        CollectionAssert.AreEqual(new byte[] { 5, 0, 0, 0 }, OdinWatchExpressions.LittleEndian(5, 4));
        CollectionAssert.AreEqual(new byte[] { 0xFF, 0xFF }, OdinWatchExpressions.LittleEndian(-1, 2));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 0, 0, 0, 0, 0, 0 }, OdinWatchExpressions.LittleEndian(0x0201, 8));
    }
}
