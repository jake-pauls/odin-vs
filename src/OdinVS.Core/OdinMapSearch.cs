namespace OdinVS.Core;

// Searching the memory of a `map[K]V` for a key without Odin's hasher (see OdinMapLayout for how the arrays are laid out): the
// used slots come from the hash array and each stored key is compared with the typed one, byte for byte. Pure, so it is tested
// with synthetic arrays; the debugger component supplies the bytes it read from the debuggee.
public static class OdinMapSearch
{
    // `Slot` is the first used slot whose key matches (null if none). `Used` counts every used slot of the map, `TextReads` the
    // string bodies that were read, `Unreadable` the string keys whose text could not be read, and `ReadLimitReached` says the
    // search stopped reading string keys before it had looked at all of them.
    public sealed record Outcome(ulong? Slot, int Used, int TextReads, int Unreadable, bool ReadLimitReached);

    // `hashBytes` holds `capacity` hashes of 8 bytes, `keyBytes` the keys array (cells of `keySize`-byte keys, see
    // OdinMapLayout.ElementOffset). A string key is a 16-byte header (data pointer, length): only keys whose length equals the
    // target's are read, through `readText(address, length)` (null when unreadable), at most `maxTextReads` times.
    public static Outcome Find(
        byte[] hashBytes,
        byte[] keyBytes,
        ulong capacity,
        ulong keySize,
        bool isString,
        byte[] target,
        Func<ulong, int, byte[]?> readText,
        int maxTextReads)
    {
        ulong? found = null;
        int used = 0;
        int reads = 0;
        int unreadable = 0;
        bool limitReached = false;
        ulong stride = isString ? 16UL : keySize;
        for (ulong i = 0; i < capacity; i++)
        {
            if (i * 8 + 8 > (ulong)hashBytes.Length || !OdinMapLayout.IsUsedHash(BitConverter.ToUInt64(hashBytes, (int)(i * 8))))
            {
                continue;
            }

            used++;
            if (found is not null)
            {
                continue;
            }

            ulong offset = OdinMapLayout.ElementOffset(i, keySize);
            if (offset + stride > (ulong)keyBytes.Length)
            {
                continue;
            }

            if (!isString)
            {
                if ((ulong)target.Length == keySize && BytesEqual(keyBytes, (int)offset, target))
                {
                    found = i;
                }

                continue;
            }

            if (BitConverter.ToInt64(keyBytes, (int)offset + 8) != target.Length)
            {
                continue;
            }

            if (target.Length == 0)
            {
                found = i;
                continue;
            }

            if (reads >= maxTextReads)
            {
                limitReached = true;
                continue;
            }

            reads++;
            byte[]? stored = readText(BitConverter.ToUInt64(keyBytes, (int)offset), target.Length);
            if (stored is null)
            {
                unreadable++;
            }
            else if (stored.Length == target.Length && BytesEqual(stored, 0, target))
            {
                found = i;
            }
        }

        return new Outcome(found, used, reads, unreadable, limitReached && found is null);
    }

    private static bool BytesEqual(byte[] buffer, int offset, byte[] expected)
    {
        for (int i = 0; i < expected.Length; i++)
        {
            if (buffer[offset + i] != expected[i])
            {
                return false;
            }
        }

        return true;
    }
}
