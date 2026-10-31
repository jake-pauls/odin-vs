using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using OdinVS.Core;

namespace OdinVS.LanguageServer;

internal sealed record StartedOls(Process Process, string OdinPath, OlsSettings Settings);

// Resolves odin and ols and starts ols with stdio piped. Returns null (after logging) when it cannot.
internal sealed class OlsProcess(Log log)
{
    private const string OdinFallback = @"C:\Program Files\Odin\odin\dist\odin.exe";

    // Lives for the VS process; the OS closes the handle on exit, which kills ols.
    private static readonly KillOnCloseJob Job = new();

    public StartedOls? Start()
    {
        string extensionDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        ToolLocator locator = new(File.Exists);

        string? ols = locator.Find("ols.exe", [Path.Combine(extensionDirectory, "ols")], []);
        if (ols is null)
        {
            log.Write($"ols.exe not found under {extensionDirectory}{Path.DirectorySeparatorChar}ols; language server disabled.");
            return null;
        }

        string[] pathDirectories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator);
        string? odin = locator.Find("odin.exe", pathDirectories, [OdinFallback]);
        if (odin is null)
        {
            log.Write($"odin.exe not found on PATH or at {OdinFallback}; language server disabled.");
            return null;
        }

        ProcessStartInfo startInfo = new(ols)
        {
            WorkingDirectory = Path.GetDirectoryName(ols)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        Process process = new() { StartInfo = startInfo, EnableRaisingEvents = true };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                log.Write($"ols stderr: {e.Data}");
            }
        };
        process.Exited += (_, _) => log.Write($"ols exited with code {process.ExitCode}");

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            log.Write($"failed to start ols: {ex.Message}; language server disabled.");
            return null;
        }

        try
        {
            Job.Add(process);
        }
        catch (InvalidOperationException ex)
        {
            log.Write($"could not tie ols to the VS process lifetime: {ex.Message}");
        }

        process.BeginErrorReadLine();
        log.Write($"ols started (pid {process.Id}) from {ols} using odin {odin}");

        // Defaults for now; a later milestone fills these from .odinproj properties.
        return new StartedOls(process, odin, new OlsSettings(odin, [], []));
    }

    public string? ReadOdinVersion(string odin)
    {
        try
        {
            using Process process = Process.Start(new ProcessStartInfo(odin, "version")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            })!;
            string output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(5000))
            {
                process.Kill();
            }

            return output;
        }
        catch (Win32Exception ex)
        {
            log.Write($"could not run 'odin version': {ex.Message}");
            return null;
        }
    }
}
