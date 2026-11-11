namespace OdinVS.Core.Tests;

[TestClass]
public sealed class OdinMapSearchTests
{
    private const ulong Capacity = 8;

    // Hashes and keys arrays as the runtime lays them out for `Capacity` slots of `keySize`-byte keys.
    private static (byte[] Hashes, byte[] Keys) Build(ulong keySize, params (int Slot, ulong Hash, byte[] Key)[] entries)
    {
        byte[] hashes = new byte[Capacity * 8];
        byte[] keys = new byte[OdinMapLayout.ElementOffset(Capacity, keySize)];
        foreach ((int slot, ulong hash, byte[] key) in entries)
        {
            BitConverter.GetBytes(hash).CopyTo(hashes, slot * 8);
            key.CopyTo(keys, (int)OdinMapLayout.ElementOffset((ulong)slot, keySize));
        }

        return (hashes, keys);
    }

    private static byte[] Int(long value) => BitConverter.GetBytes(value);

    private static byte[]? NoText(ulong address, int length) => throw new AssertFailedException("no text should be read");

    [TestMethod]
    public void FindsAnIntegerKeyAndCountsTheUsedSlots()
    {
        (byte[] hashes, byte[] keys) = Build(8, (0, 5, Int(10)), (3, 6, Int(30)), (6, 7, Int(60)));
        OdinMapSearch.Outcome outcome = OdinMapSearch.Find(hashes, keys, Capacity, 8, false, Int(30), NoText, 10);
        Assert.AreEqual(3UL, outcome.Slot);
        Assert.AreEqual(3, outcome.Used);
    }

    [TestMethod]
    public void MissingKeyGivesNoSlot()
    {
        (byte[] hashes, byte[] keys) = Build(8, (0, 5, Int(10)), (3, 6, Int(30)));
        OdinMapSearch.Outcome outcome = OdinMapSearch.Find(hashes, keys, Capacity, 8, false, Int(31), NoText, 10);
        Assert.IsNull(outcome.Slot);
        Assert.AreEqual(2, outcome.Used);
    }

    [TestMethod]
    public void DeletedAndEmptySlotsAreNotMatched()
    {
        ulong tombstone = (1UL << 63) | 9;
        (byte[] hashes, byte[] keys) = Build(8, (1, tombstone, Int(77)), (2, 0, Int(78)), (4, 3, Int(79)));
        Assert.IsNull(OdinMapSearch.Find(hashes, keys, Capacity, 8, false, Int(77), NoText, 10).Slot, "a deleted slot keeps its old key");
        Assert.IsNull(OdinMapSearch.Find(hashes, keys, Capacity, 8, false, Int(78), NoText, 10).Slot, "an empty slot has hash 0");
        Assert.AreEqual(4UL, OdinMapSearch.Find(hashes, keys, Capacity, 8, false, Int(79), NoText, 10).Slot);
    }

    [TestMethod]
    public void KeysThatShareACellAreReadFromTheirOwnOffsets()
    {
        // 16-byte keys: four per 64-byte cell, so slot 5 is in the second cell at offset 16.
        byte[] key = new byte[16];
        BitConverter.GetBytes(0x1122334455667788L).CopyTo(key, 0);
        BitConverter.GetBytes(0x99L).CopyTo(key, 8);
        (byte[] hashes, byte[] keys) = Build(16, (5, 4, key));
        Assert.AreEqual(5UL, OdinMapSearch.Find(hashes, keys, Capacity, 16, false, key, NoText, 10).Slot);
        byte[] other = (byte[])key.Clone();
        other[15] = 1;
        Assert.IsNull(OdinMapSearch.Find(hashes, keys, Capacity, 16, false, other, NoText, 10).Slot);
    }

    [TestMethod]
    public void TargetOfAnotherSizeNeverMatches()
    {
        (byte[] hashes, byte[] keys) = Build(8, (0, 5, Int(10)));
        Assert.IsNull(OdinMapSearch.Find(hashes, keys, Capacity, 8, false, [10, 0, 0, 0], NoText, 10).Slot);
    }

    private static byte[] StringHeader(ulong data, long length)
    {
        byte[] header = new byte[16];
        BitConverter.GetBytes(data).CopyTo(header, 0);
        BitConverter.GetBytes(length).CopyTo(header, 8);
        return header;
    }

    [TestMethod]
    public void StringKeysAreReadOnlyWhenTheirLengthMatches()
    {
        Dictionary<ulong, byte[]> memory = new() { [0x1000] = "red"u8.ToArray(), [0x2000] = "green"u8.ToArray(), [0x3000] = "bad"u8.ToArray() };
        (byte[] hashes, byte[] keys) = Build(16, (0, 5, StringHeader(0x1000, 3)), (1, 6, StringHeader(0x2000, 5)), (2, 7, StringHeader(0x3000, 3)));
        List<ulong> reads = [];
        Func<ulong, int, byte[]?> read = (address, length) =>
        {
            reads.Add(address);
            return memory[address];
        };

        OdinMapSearch.Outcome outcome = OdinMapSearch.Find(hashes, keys, Capacity, 16, true, "bad"u8.ToArray(), read, 10);
        Assert.AreEqual(2UL, outcome.Slot);
        CollectionAssert.AreEqual(new ulong[] { 0x1000, 0x3000 }, reads, "the 5-byte key is never read");
        Assert.AreEqual(2, outcome.TextReads);
        Assert.AreEqual(3, outcome.Used);
    }

    [TestMethod]
    public void EmptyStringKeyNeedsNoRead()
    {
        (byte[] hashes, byte[] keys) = Build(16, (4, 5, StringHeader(0, 0)));
        Assert.AreEqual(4UL, OdinMapSearch.Find(hashes, keys, Capacity, 16, true, [], NoText, 10).Slot);
    }

    [TestMethod]
    public void UnreadableStringKeysAreCountedAndSkipped()
    {
        (byte[] hashes, byte[] keys) = Build(16, (0, 5, StringHeader(0xDEAD, 3)), (1, 6, StringHeader(0x2000, 3)));
        Func<ulong, int, byte[]?> read = (address, length) => address == 0x2000 ? "yes"u8.ToArray() : null;
        OdinMapSearch.Outcome outcome = OdinMapSearch.Find(hashes, keys, Capacity, 16, true, "yes"u8.ToArray(), read, 10);
        Assert.AreEqual(1UL, outcome.Slot);
        Assert.AreEqual(1, outcome.Unreadable);
    }

    [TestMethod]
    public void ReadLimitStopsTheSearchAndSaysSo()
    {
        (byte[] hashes, byte[] keys) = Build(16, (0, 5, StringHeader(0x1000, 3)), (1, 6, StringHeader(0x2000, 3)), (2, 7, StringHeader(0x3000, 3)));
        int reads = 0;
        Func<ulong, int, byte[]?> read = (address, length) =>
        {
            reads++;
            return "xxx"u8.ToArray();
        };

        OdinMapSearch.Outcome outcome = OdinMapSearch.Find(hashes, keys, Capacity, 16, true, "yes"u8.ToArray(), read, 2);
        Assert.IsNull(outcome.Slot);
        Assert.IsTrue(outcome.ReadLimitReached);
        Assert.AreEqual(2, reads);
        Assert.AreEqual(3, outcome.Used);
    }

    [TestMethod]
    public void FoundBeforeTheLimitIsNotReportedAsLimited()
    {
        (byte[] hashes, byte[] keys) = Build(16, (0, 5, StringHeader(0x1000, 3)), (1, 6, StringHeader(0x2000, 3)));
        Func<ulong, int, byte[]?> read = (address, length) => "yes"u8.ToArray();
        OdinMapSearch.Outcome outcome = OdinMapSearch.Find(hashes, keys, Capacity, 16, true, "yes"u8.ToArray(), read, 1);
        Assert.AreEqual(0UL, outcome.Slot);
        Assert.IsFalse(outcome.ReadLimitReached);
    }
}
