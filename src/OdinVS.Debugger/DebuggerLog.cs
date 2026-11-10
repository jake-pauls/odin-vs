using System;
using System.Diagnostics;
using System.IO;

namespace OdinVS.Debugger;

// Log of the debugger component, `%LOCALAPPDATA%\OdinVS\odinvs-debugger.log`. A separate file from the language client's
// `odinvs.log`: the component is loaded by the debugger, possibly in another process, and every line carries the process id.
// When the file passes MaxBytes it is moved to `odinvs-debugger.log.1` (replacing the previous one), so the two files together
// stay under twice that size.
internal static class DebuggerLog
{
    public const string FileName = "odinvs-debugger.log";

    private const long MaxBytes = 1024 * 1024;

    private static readonly object m_Lock = new();
    private static readonly int m_ProcessId = GetProcessId();
    private static string? m_Path;

    private static int GetProcessId()
    {
        using Process current = Process.GetCurrentProcess();
        return current.Id;
    }

    public static void Write(string message)
    {
        try
        {
            lock (m_Lock)
            {
                // Computed here, not in a static initializer: a failure there would throw from every call.
                m_Path ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OdinVS", FileName);
                Directory.CreateDirectory(Path.GetDirectoryName(m_Path)!);
                Rotate(m_Path);
                File.AppendAllText(m_Path, $"{DateTime.Now:HH:mm:ss.fff} [{m_ProcessId}] {message}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
            // The logger itself failing (disk, lock) must not take the debugger down; there is nowhere else to report it.
        }
    }

    private static void Rotate(string path)
    {
        FileInfo info = new(path);
        if (info.Exists && info.Length > MaxBytes)
        {
            string older = path + ".1";
            if (File.Exists(older))
            {
                File.Delete(older);
            }

            File.Move(path, older);
        }
    }
}
