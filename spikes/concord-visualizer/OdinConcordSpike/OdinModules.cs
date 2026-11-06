using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.CallStack;

namespace OdinConcordSpike;

// Decides whether a module was compiled from Odin. The evaluator is registered for language C, which MSVC C code and Odin
// share, and `CompilerId` is empty for both (M5.5 Phase F, FINDINGS.md), so the module's symbol file is searched for a
// source document path ending in `.odin`: a PDB lists every source path as NUL-terminated bytes (checked on real Odin PDBs:
// every path appears three times, and Odin's own base/core paths are always there). The terminating NUL is part of the
// pattern, so `.odinproj`, `.odinfile`, `x.odin\` and the `[file.odin]` spelling inside symbol names do not match.
// The answer is cached per module once it is definitive; a missing or unreadable symbol file is retried on the next frame.
// A module that mixes Odin with C objects counts as Odin as a whole.
public static class OdinModules
{
    private static readonly byte[] m_Extension = [.. System.Text.Encoding.ASCII.GetBytes(".odin"), 0];
    private static readonly ConcurrentDictionary<Guid, bool> m_Cache = new();
    private static readonly ConcurrentDictionary<Guid, bool> m_LoggedUndecided = new();

    // The scan runs on the dispatcher thread, once per module, and is never capped (a cap would turn a big Odin PDB into a
    // silent "not Odin"). It stops at the first match, which in an Odin PDB is about a fifth of the way in; only a big
    // non-Odin language-C module reads the whole file: about 0.4 s per GB warm, several seconds cold on this machine.
    // A scan slower than this is logged as a warning with the file size.
    private const double SlowScanMilliseconds = 1000;

    public static bool IsOdinFrame(DkmStackWalkFrame? frame)
    {
        DkmModuleInstance? instance = frame?.ModuleInstance;
        if (instance is null)
        {
            return false;
        }

        if (m_Cache.TryGetValue(instance.UniqueId, out bool known))
        {
            return known;
        }

        if (Decide(instance) is bool answer)
        {
            m_Cache[instance.UniqueId] = answer;
            return answer;
        }

        return false;
    }

    // Null means undecided (no symbol file yet, or it could not be read): not Odin for now, asked again on the next frame.
    private static bool? Decide(DkmModuleInstance instance)
    {
        long started = Stopwatch.GetTimestamp();
        string? path = null;
        try
        {
            path = instance.Module?.GetSymbolFilePath();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                LogUndecided(instance, $"symbols {path ?? "(none)"} not available yet");
                return null;
            }

            bool odin = FileContains(path!, m_Extension);
            double ms = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            SpikeLog.Write($"odin module check: {instance.FullName} (symbols {path}) -> {(odin ? "Odin" : "not Odin")} in {ms:F1} ms");
            if (ms > SlowScanMilliseconds)
            {
                SpikeLog.Write($"WARNING: the Odin check of {instance.FullName} took {ms / 1000:F1} s for a {new FileInfo(path!).Length / (1024 * 1024)} MB symbol file; Visual Studio may have paused while the first locals were shown (once per module and session)");
            }

            return odin;
        }
        catch (Exception ex)
        {
            // Not Odin for now: the component then leaves the module to the native evaluator, and the check is repeated.
            LogUndecided(instance, $"failed on {path ?? "(none)"}: {ex.Message}");
            return null;
        }
    }

    private static void LogUndecided(DkmModuleInstance instance, string reason)
    {
        if (m_LoggedUndecided.TryAdd(instance.UniqueId, true))
        {
            SpikeLog.Write($"odin module check for {instance.FullName} undecided, treating as not Odin until it can be read: {reason}");
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
