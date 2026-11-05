using System;
using System.IO;

namespace OdinConcordSpike;

// Throwaway log for the M5.5 spike; written from inside the debugger component, read by hand.
public static class SpikeLog
{
    private static readonly string m_Path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OdinVS", "concord-spike.log");

    private static readonly object m_Lock = new();

    public static void Write(string message)
    {
        try
        {
            lock (m_Lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(m_Path)!);
                File.AppendAllText(m_Path, $"{DateTime.Now:HH:mm:ss.fff} [{System.Diagnostics.Process.GetCurrentProcess().Id}] {message}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
            // The logger itself failing (disk, lock) must not take the debugger down; nothing else to do.
        }
    }
}
