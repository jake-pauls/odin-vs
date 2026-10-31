using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using OdinVS.Core;

namespace OdinVS.LanguageServer;

internal sealed record StartedOls(Process Process, string OdinPath, OlsSettings Settings);

// Resolves odin and ols and starts ols with stdio piped. Returns null (after logging) when it cannot.
internal sealed class OlsProcess(Log log)
{
    // Lives for the VS process; the OS closes the handle on exit, which kills ols. Null if the job could not be created.
    private static readonly Lazy<KillOnCloseJob?> Job = new(CreateJob);

    public StartedOls? Start()
    {
        string extensionDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        ResolvedTools tools = new ToolResolver(new ToolLocator(File.Exists))
            .Resolve(extensionDirectory, Environment.GetEnvironmentVariable("PATH"));
        if (tools.Ols is null || tools.Odin is null)
        {
            foreach (string problem in tools.Problems)
            {
                log.Write($"{problem}; language server disabled.");
            }

            return null;
        }

        string ols = tools.Ols;
        string odin = tools.Odin;
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
        process.Exited += (_, _) => LogExit(process);

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
            Job.Value?.Add(process);
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
            using Process? process = Process.Start(new ProcessStartInfo(odin, "version")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            });
            if (process is null)
            {
                return null;
            }

            StringBuilder output = new();
            process.OutputDataReceived += (_, e) =>
            {
                lock (output)
                {
                    output.AppendLine(e.Data);
                }
            };
            process.BeginOutputReadLine();
            if (!process.WaitForExit(5000))
            {
                process.Kill();
                log.Write("'odin version' did not finish within 5 seconds");
                return null;
            }

            // The parameterless overload waits for the redirected output to be fully consumed.
            process.WaitForExit();
            lock (output)
            {
                return output.ToString();
            }
        }
        catch (Win32Exception ex)
        {
            log.Write($"could not run 'odin version': {ex.Message}");
            return null;
        }
        catch (InvalidOperationException ex)
        {
            log.Write($"could not run 'odin version': {ex.Message}");
            return null;
        }
    }

    // Runs on a threadpool thread, possibly after StopProcess disposed the process, so it must not throw.
    private void LogExit(Process process)
    {
        try
        {
            log.Write($"ols exited with code {process.ExitCode}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            log.Write("ols exited");
        }
    }

    private static KillOnCloseJob? CreateJob()
    {
        try
        {
            return new KillOnCloseJob();
        }
        catch (InvalidOperationException ex)
        {
            Log.Default.Write($"could not create the ols job object, ols may outlive VS: {ex.Message}");
            return null;
        }
    }
}
