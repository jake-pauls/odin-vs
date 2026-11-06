using System;
using System.IO;

namespace OdinConcordSpike;

// Throwaway log for the M5.5 spike; written from inside the debugger component, read by hand.
public static class SpikeLog
{
    private static readonly object m_Lock = new();
    private static readonly int m_ProcessId = GetProcessId();
    private static string? m_Path;

    private static int GetProcessId()
    {
        using System.Diagnostics.Process current = System.Diagnostics.Process.GetCurrentProcess();
        return current.Id;
    }

    public static void Write(string message)
    {
        try
        {
            lock (m_Lock)
            {
                // Computed here, not in a static initializer: a failure there would throw from every call.
                m_Path ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OdinVS", "concord-spike.log");
                Directory.CreateDirectory(Path.GetDirectoryName(m_Path)!);
                File.AppendAllText(m_Path, $"{DateTime.Now:HH:mm:ss.fff} [{m_ProcessId}] {message}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
            // The logger itself failing (disk, lock) must not take the debugger down; nothing else to do.
        }
    }
}
