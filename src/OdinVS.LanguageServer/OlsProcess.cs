using System.Diagnostics;
using System.Reflection;
using OdinVS.Core;

namespace OdinVS.LanguageServer;

// Resolves odin and ols, writes ols.json and starts ols with stdio piped. Returns null (after logging) when it cannot.
internal sealed class OlsProcess(Log log)
{
    private const string OdinFallback = @"C:\Program Files\Odin\odin\dist\odin.exe";

    public Process? Start()
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

        string configDirectory = Path.Combine(Log.DataDirectory, "ols");
        Directory.CreateDirectory(configDirectory);
        OlsSettings settings = new(odin, [], []);
        File.WriteAllText(Path.Combine(configDirectory, "ols.json"), OlsConfig.Generate(settings));

        ProcessStartInfo startInfo = new(ols)
        {
            WorkingDirectory = configDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        Process process = new() { StartInfo = startInfo, EnableRaisingEvents = true };
        process.ErrorDataReceived += (_, e) => OnStderrLine(e.Data, odin);
        process.Exited += (_, _) => log.Write($"ols exited with code {process.ExitCode}");

        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            log.Write($"failed to start ols: {ex.Message}; language server disabled.");
            return null;
        }

        process.BeginErrorReadLine();
        log.Write($"ols started (pid {process.Id}) from {ols} using odin {odin}");
        return process;
    }

    private void OnStderrLine(string? line, string odin)
    {
        if (line is null)
        {
            return;
        }

        log.Write($"ols: {line}");
        if (line.Contains("Starting Odin Language Server"))
        {
            string? warning = VersionCompatibility.Check(ReadOdinVersion(odin), line);
            if (warning is not null)
            {
                log.Write($"WARNING: {warning}");
            }
        }
    }

    private string? ReadOdinVersion(string odin)
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
        catch (System.ComponentModel.Win32Exception ex)
        {
            log.Write($"could not run 'odin version': {ex.Message}");
            return null;
        }
    }
}
