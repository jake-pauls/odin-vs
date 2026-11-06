using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace OdinVS.Core.Tests;

// Guards the debugger visualizer's decoding of Odin maps (OdinMapLayout), slices and strings (milestone item G6).
// The decoding depends on Odin's base library, so these are the first tests to run when Odin is upgraded:
//  - tier 1: the formulas on known values;
//  - RequiresToolchain: the installed Odin's base/runtime sources still contain the constructs the decoder assumes;
//  - RequiresToolchain: a real Odin program dumps where it puts keys, values and hashes for many key/value sizes and
//    every number is compared with the formulas.
// A failure means the visualizer would show wrong or missing entries after the upgrade: update OdinMapLayout and the
// "Odin-version-coupled code" table in spikes/concord-visualizer/FINDINGS.md, then re-run the Exp checklist.
[TestClass]
public sealed class OdinMapLayoutTests
{
    // Assumes Odin is installed at the default location, like OlsProtocolTests.
    private const string OdinPath = ToolResolver.OdinFallback;

    [TestMethod]
    [DataRow(0UL, 12UL, 0UL)]
    [DataRow(1UL, 1UL, 1UL)]
    [DataRow(64UL, 1UL, 64UL)]
    [DataRow(3UL, 5UL, 15UL)]
    [DataRow(12UL, 5UL, 64UL)]
    [DataRow(21UL, 3UL, 64UL)]
    [DataRow(8UL, 12UL, 100UL)]
    [DataRow(5UL, 100UL, 640UL)]
    [DataRow(2UL, 64UL, 128UL)]
    [DataRow(2UL, 65UL, 256UL)]
    [DataRow(3UL, 0UL, 0UL)]
    [DataRow(2UL, 128UL, 256UL)]
    [DataRow(2UL, 129UL, 384UL)]
    [DataRow(25UL, 5UL, 133UL)]
    public void ElementOffsetSkipsCellPadding(ulong index, ulong size, ulong expected)
    {
        Assert.AreEqual(expected, OdinMapLayout.ElementOffset(index, size));
    }

    [TestMethod]
    [DataRow(0UL, 0UL, 1UL)]
    [DataRow(1UL, 64UL, 64UL)]
    [DataRow(63UL, 64UL, 1UL)]
    [DataRow(64UL, 64UL, 1UL)]
    [DataRow(65UL, 128UL, 1UL)]
    [DataRow(128UL, 128UL, 1UL)]
    public void CellHoldsWholeElementsInCacheLineMultiples(ulong size, ulong expectedCell, ulong expectedPerCell)
    {
        Assert.AreEqual(expectedCell, OdinMapLayout.CellSize(size));
        Assert.AreEqual(expectedPerCell, OdinMapLayout.ElementsPerCell(size));
    }

    [TestMethod]
    public void DataWordHoldsLog2CapacityInItsLowBits()
    {
        Assert.AreEqual(0UL, OdinMapLayout.Capacity(0));
        Assert.AreEqual(8UL, OdinMapLayout.Capacity(0x1000UL | 3));
        Assert.AreEqual(32UL, OdinMapLayout.Capacity(0x2000UL | 5));
        Assert.AreEqual(0x1000UL, OdinMapLayout.KeysAddress(0x1000UL | 3));
    }

    [TestMethod]
    public void EmptyAndDeletedSlotsAreNotUsed()
    {
        Assert.IsFalse(OdinMapLayout.IsUsedHash(0));
        Assert.IsTrue(OdinMapLayout.IsUsedHash(1));
        Assert.IsTrue(OdinMapLayout.IsUsedHash(ulong.MaxValue >> 1));
        Assert.IsFalse(OdinMapLayout.IsUsedHash(1UL << 63));
        Assert.IsFalse(OdinMapLayout.IsUsedHash((1UL << 63) | 5));
    }

    [TestMethod]
    public void ArraysFollowEachOtherByCapacity()
    {
        OdinMapLayout.MapArrays arrays = OdinMapLayout.Arrays(0x1000UL | 3, keySize: 12, valueSize: 4);
        Assert.AreEqual(8UL, arrays.Capacity);
        Assert.AreEqual(0x1000UL, arrays.Keys);
        Assert.AreEqual(0x1000UL + 100, arrays.Values);
        Assert.AreEqual(0x1000UL + 100 + 32, arrays.Hashes);
    }

    [TestMethod]
    [TestCategory("RequiresToolchain")]
    public void InstalledOdinRuntimeStillHasTheConstructsTheDecoderAssumes()
    {
        string runtime = RuntimeDirectory();
        string core = Normalized(File.ReadAllText(Path.Combine(runtime, "core.odin")));
        string maps = Normalized(File.ReadAllText(Path.Combine(runtime, "dynamic_map_internal.odin")));

        // Slices, dynamic arrays, strings: field names and order (the visualizer reads `.data`, `.len`, `.cap` and the
        // header at offsets 8 and 16).
        AssertContains(core, "Raw_Slice :: struct { data: rawptr, len: int, }");
        AssertContains(core, "Raw_Dynamic_Array :: struct { data: rawptr, len: int, cap: int, allocator: Allocator, }");
        AssertContains(core, "Raw_String :: struct { data: [^]byte, len: int, }");

        // Maps: the header, the cell size, the cell description.
        AssertContains(core, "Raw_Map :: struct { data: uintptr, len: uintptr, allocator: Allocator, }");
        AssertContains(core, "MAP_CACHE_LINE_LOG2 :: 6");
        AssertContains(core, "MAP_CACHE_LINE_SIZE :: 1 << MAP_CACHE_LINE_LOG2");
        AssertContains(core, "Map_Hash :: uintptr");
        AssertContains(core, "Map_Cell_Info :: struct { size_of_type: uintptr, align_of_type: uintptr, size_of_cell: uintptr, elements_per_cell: uintptr, }");
        AssertContains(core, "Map_Cell :: struct($T: typeid) #align(MAP_CACHE_LINE_SIZE) { data: [MAP_CACHE_LINE_SIZE / size_of(T) when 0 < size_of(T) && size_of(T) < MAP_CACHE_LINE_SIZE else 1]T, }");

        // Capacity bits, tombstones, element indexing, and the order of the arrays.
        AssertContains(maps, "return m.data & (64 - 1)");
        AssertContains(maps, "return m.data &~ uintptr(64 - 1)");
        AssertContains(maps, "TOMBSTONE_MASK :: 1<<(size_of(Map_Hash)*8 - 1)");
        AssertContains(maps, "return (hash != 0) & (hash & TOMBSTONE_MASK == 0)");
        AssertContains(maps, "stride := info.size_of_cell if n == 1 else info.size_of_type");
        AssertContains(maps, "padding := info.size_of_cell - n*stride");
        AssertContains(maps, "vs = map_cell_index_dynamic(ks, info.ks, capacity)");
        AssertContains(maps, "hs_ := map_cell_index_dynamic(vs, info.vs, capacity)");
    }

    [TestMethod]
    [TestCategory("RequiresToolchain")]
    public void RealMapLayoutsMatchTheDecodingMath()
    {
        List<MapDump> dumps = RunLayoutDump();
        Assert.IsGreaterThanOrEqualTo(10, dumps.Count, "the dump program printed too few maps");

        foreach (MapDump dump in dumps)
        {
            string label = $"map with {dump.KeySize}-byte keys and {dump.ValueSize}-byte values";
            OdinMapLayout.MapArrays arrays = OdinMapLayout.Arrays(dump.Data, dump.KeySize, dump.ValueSize);

            Assert.AreEqual(dump.Capacity, arrays.Capacity, label + ": capacity");
            Assert.AreEqual(dump.KeysAddress, arrays.Keys, label + ": address of the keys array (the data word without its capacity bits)");
            Assert.AreEqual(dump.Values, arrays.Values - arrays.Keys, label + ": offset of the values array");
            Assert.AreEqual(dump.Hashes, arrays.Hashes - arrays.Keys, label + ": offset of the hashes array");
            Assert.AreEqual(dump.KeyCell, OdinMapLayout.CellSize(dump.KeySize), label + ": key cell size");
            Assert.AreEqual(dump.KeyPerCell, OdinMapLayout.ElementsPerCell(dump.KeySize), label + ": keys per cell");
            Assert.AreEqual(dump.ValueCell, OdinMapLayout.CellSize(dump.ValueSize), label + ": value cell size");
            Assert.AreEqual(dump.ValuePerCell, OdinMapLayout.ElementsPerCell(dump.ValueSize), label + ": values per cell");

            Assert.HasCount((int)dump.Length, dump.Slots, label + ": number of used slots");
            foreach (SlotDump slot in dump.Slots)
            {
                Assert.IsTrue(OdinMapLayout.IsUsedHash(slot.Hash), label + $": slot {slot.Index} should count as used");
                Assert.AreEqual(slot.KeyOffset, OdinMapLayout.ElementOffset(slot.Index, dump.KeySize), label + $": key offset of slot {slot.Index}");
                Assert.AreEqual(slot.ValueOffset, OdinMapLayout.ElementOffset(slot.Index, dump.ValueSize), label + $": value offset of slot {slot.Index}");
            }

            foreach (ulong tombstone in dump.DeletedHashes)
            {
                Assert.IsFalse(OdinMapLayout.IsUsedHash(tombstone), label + ": a deleted slot must not count as used");
            }
        }

        Assert.IsTrue(dumps.Any(d => d.DeletedHashes.Count > 0), "the dump program produced no deleted slots, so tombstones were not exercised");
        Assert.IsTrue(dumps.Any(d => d.ValueSize == 0), "the zero-size value case (map[K]struct{}) is missing");
        Assert.IsTrue(dumps.Any(d => d.KeySize == 12 && d.Capacity == 8), "the 12-byte key, capacity-8 case (array after keys starts at byte 100) is missing");
    }

    private static string RuntimeDirectory()
    {
        RequireOdin();
        // odin.exe lives in <Odin>\odin\dist, next to base, core and vendor.
        string dist = Path.GetDirectoryName(OdinPath)!;
        string runtime = Path.Combine(dist, "base", "runtime");
        Assert.IsTrue(Directory.Exists(runtime), $"{runtime} not found: Odin's source layout changed, update this test");
        return runtime;
    }

    private static void RequireOdin()
    {
        if (!File.Exists(OdinPath))
        {
            Assert.Inconclusive("Odin is not installed at the default location.");
        }
    }

    // Comments removed and whitespace collapsed, so the checks do not depend on formatting.
    private static string Normalized(string source)
    {
        string withoutLineComments = Regex.Replace(source, @"//[^\r\n]*", string.Empty);
        string withoutComments = Regex.Replace(withoutLineComments, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return Regex.Replace(withoutComments, @"\s+", " ");
    }

    private static void AssertContains(string source, string expected)
    {
        Assert.IsTrue(
            source.Contains(expected, StringComparison.Ordinal),
            $"Odin's base library no longer contains `{expected}`. The debugger visualizer's decoding (OdinMapLayout and the visualizer in spikes/concord-visualizer) depends on it: update both, FINDINGS.md, and re-run the Exp checklist.");
    }

    private static List<MapDump> RunLayoutDump()
    {
        RequireOdin();
        string source = Path.Combine(RepoPaths.Root, "tests", "samples", "DebuggerLayout", "layout_dump.odin");
        string work = Directory.CreateTempSubdirectory("odinvs-layout").FullName;
        try
        {
            return RunLayoutDumpIn(work, source);
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup of our own temp folder (the exe may still be locked); it must not hide the test's own failure.
            }
        }
    }

    private static List<MapDump> RunLayoutDumpIn(string work, string source)
    {
        ProcessStartInfo start = new(OdinPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = work,
        };
        start.ArgumentList.Add("run");
        start.ArgumentList.Add(source);
        start.ArgumentList.Add("-file");
        start.ArgumentList.Add("-out:" + Path.Combine(work, "layout_dump.exe"));

        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(2)))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("odin run on the layout dump program did not finish in 2 minutes");
        }

        Assert.AreEqual(0, process.ExitCode, "odin run failed: " + error.Result + output.Result);
        return Parse(output.Result);
    }

    private static List<MapDump> Parse(string text)
    {
        List<MapDump> dumps = [];
        MapDump? current = null;
        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts[0] is "LAYOUT")
            {
                Dictionary<string, ulong> values = KeyValues(parts.Skip(1));
                current = new MapDump(
                    values["key"], values["value"], values["data"], values["len"], values["cap"], values["keys_address"], values["values"], values["hashes"],
                    values["key_cell"], values["key_per_cell"], values["value_cell"], values["value_per_cell"]);
                dumps.Add(current);
            }
            else if (parts[0] is "SLOT" && current is not null)
            {
                Dictionary<string, ulong> values = KeyValues(parts.Skip(2));
                current.Slots.Add(new SlotDump(ulong.Parse(parts[1], CultureInfo.InvariantCulture), values["hash"], values["key_offset"], values["value_offset"]));
            }
            else if (parts[0] is "DELETED" && current is not null)
            {
                current.DeletedHashes.Add(KeyValues(parts.Skip(2))["hash"]);
            }
        }

        return dumps;
    }

    private static Dictionary<string, ulong> KeyValues(IEnumerable<string> parts)
    {
        Dictionary<string, ulong> values = [];
        foreach (string part in parts)
        {
            int equals = part.IndexOf('=');
            if (equals > 0)
            {
                values[part[..equals]] = ulong.Parse(part[(equals + 1)..], CultureInfo.InvariantCulture);
            }
        }

        return values;
    }

    private sealed record SlotDump(ulong Index, ulong Hash, ulong KeyOffset, ulong ValueOffset);

    private sealed record MapDump(
        ulong KeySize,
        ulong ValueSize,
        ulong Data,
        ulong Length,
        ulong Capacity,
        ulong KeysAddress,
        ulong Values,
        ulong Hashes,
        ulong KeyCell,
        ulong KeyPerCell,
        ulong ValueCell,
        ulong ValuePerCell)
    {
        public List<SlotDump> Slots { get; } = [];

        public List<ulong> DeletedHashes { get; } = [];
    }
}
