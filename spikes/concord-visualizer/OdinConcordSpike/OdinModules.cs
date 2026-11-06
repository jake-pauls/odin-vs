using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.CallStack;

namespace OdinConcordSpike;

// Decides whether a module was compiled from Odin. The evaluator is registered for language C, which MSVC C code and Odin
// share, and `CompilerId` is empty for both (M5.5 Phase F, FINDINGS.md), so the module's symbol file is searched for the
// source file extension `.odin`: a PDB lists every source document path as plain bytes. The answer is cached per module.
// A module that mixes Odin with C objects counts as Odin as a whole.
public static class OdinModules
{
    private static readonly byte[] m_Extension = System.Text.Encoding.ASCII.GetBytes(".odin");
    private static readonly ConcurrentDictionary<Guid, bool> m_Cache = new();

    public static bool IsOdinFrame(DkmStackWalkFrame? frame)
    {
        DkmModuleInstance? instance = frame?.ModuleInstance;
        if (instance is null)
        {
            return false;
        }

        return m_Cache.GetOrAdd(instance.UniqueId, _ => Decide(instance));
    }

    private static bool Decide(DkmModuleInstance instance)
    {
        long started = Stopwatch.GetTimestamp();
        try
        {
            string? path = instance.Module?.GetSymbolFilePath();
            bool odin = !string.IsNullOrEmpty(path) && File.Exists(path) && FileContains(path!, m_Extension);
            double ms = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            SpikeLog.Write($"odin module check: {instance.FullName} (symbols {path ?? "(none)"}) -> {(odin ? "Odin" : "not Odin")} in {ms:F1} ms");
            return odin;
        }
        catch (Exception ex)
        {
            // Not Odin is the safe answer: the component then leaves the module to the native evaluator.
            SpikeLog.Write($"odin module check for {instance.FullName} failed, treating as not Odin: {ex.Message}");
            return false;
        }
    }

    private static int IndexOf(byte[] haystack, int length, byte[] needle)
    {
        int start = 0;
        while (start <= length - needle.Length)
        {
            int at = Array.IndexOf(haystack, needle[0], start, length - needle.Length - start + 1);
            if (at < 0)
            {
                return -1;
            }

            int k = 1;
            while (k < needle.Length && haystack[at + k] == needle[k])
            {
                k++;
            }

            if (k == needle.Length)
            {
                return at;
            }

            start = at + 1;
        }

        return -1;
    }

    private static bool FileContains(string path, byte[] needle)
    {
        const int ChunkSize = 1 << 20;
        byte[] buffer = new byte[ChunkSize + needle.Length];
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        int carried = 0;
        int read;
        while ((read = stream.Read(buffer, carried, ChunkSize)) > 0)
        {
            int total = carried + read;
            if (IndexOf(buffer, total, needle) >= 0)
            {
                return true;
            }

            carried = Math.Min(needle.Length - 1, total);
            Buffer.BlockCopy(buffer, total - carried, buffer, 0, carried);
        }

        return false;
    }
}
