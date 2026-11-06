namespace OdinVS.Core;

// How Odin's runtime lays out a `map[K]V` in memory, so a debugger visualizer can read entries from a debuggee.
// This depends on Odin's base library (`base/runtime/core.odin`: Raw_Map, Map_Cell, MAP_CACHE_LINE_SIZE, Map_Hash;
// `base/runtime/dynamic_map_internal.odin`: map_cell_index_dynamic, map_kvh_data_dynamic, map_hash_is_valid) and
// breaks silently when those change. It is the first thing to check on an Odin bump: the tests in OdinMapLayoutTests
// assert the source constructs and compare these formulas with a dump of real maps (see milestone item G6).
public static class OdinMapLayout
{
    // MAP_CACHE_LINE_SIZE: keys, values and hashes live in arrays of cells this big.
    public const ulong CacheLineSize = 64;

    // The map's `data` word is the allocation pointer with log2(capacity) in its low six bits (map_log2_cap, map_data).
    private const ulong LogCapacityMask = CacheLineSize - 1;

    // Map_Hash is a uintptr and its top bit marks a deleted slot (TOMBSTONE_MASK); zero marks an empty slot.
    private const ulong TombstoneMask = 1UL << 63;

    public static ulong Capacity(ulong data) => data == 0 ? 0 : 1UL << (int)(data & LogCapacityMask);

    public static ulong KeysAddress(ulong data) => data & ~LogCapacityMask;

    public static bool IsUsedHash(ulong hash) => hash != 0 && (hash & TombstoneMask) == 0;

    // Map_Cell_Info.elements_per_cell: as many T as fit in one cell, or one when T is zero-sized or at least a cell.
    public static ulong ElementsPerCell(ulong typeSize) => typeSize is > 0 and < CacheLineSize ? CacheLineSize / typeSize : 1;

    // Map_Cell_Info.size_of_cell: a cell is rounded up to a multiple of the cache line.
    public static ulong CellSize(ulong typeSize)
    {
        return typeSize is > 0 and < CacheLineSize
            ? CacheLineSize
            : Math.Max(CacheLineSize, (typeSize + CacheLineSize - 1) / CacheLineSize * CacheLineSize);
    }

    // map_cell_index_dynamic: the byte offset of element `index` from the start of a cell array, skipping each cell's padding.
    public static ulong ElementOffset(ulong index, ulong typeSize)
    {
        ulong perCell = ElementsPerCell(typeSize);
        return perCell == 1 ? index * CellSize(typeSize) : index / perCell * CacheLineSize + index % perCell * typeSize;
    }

    // map_kvh_data_dynamic: the values array starts after `capacity` keys, the hashes after `capacity` values.
    public static MapArrays Arrays(ulong data, ulong keySize, ulong valueSize)
    {
        ulong capacity = Capacity(data);
        ulong keys = KeysAddress(data);
        ulong values = keys + ElementOffset(capacity, keySize);
        ulong hashes = values + ElementOffset(capacity, valueSize);
        return new MapArrays(capacity, keys, values, hashes);
    }

    public sealed record MapArrays(ulong Capacity, ulong Keys, ulong Values, ulong Hashes);
}
