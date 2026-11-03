using System.Diagnostics;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace OdinVS.Core;

// Public because MSBuild instantiates tasks by reflection. Argument building and error parsing live in internal classes with unit tests.
public sealed class OdinBuild : Microsoft.Build.Utilities.Task, ICancelableTask
{
    public const string ErrorCode = "ODIN0001";
    public const string WarningCode = "ODIN0002";
    public const string FailedWithoutDiagnosticsCode = "ODIN0100";
    public const string OdinNotFoundCode = "ODIN0101";

    private readonly object m_Lock = new();
    private Process? m_Process;
    private bool m_Cancelled;

    // Explicit path to odin.exe; when empty the task searches PATH and then the default install location.
    public string OdinExe { get; set; } = string.Empty;

    [Required]
    public string PackageDirectory { get; set; } = string.Empty;

    [Required]
    public string OutputPath { get; set; } = string.Empty;

    public bool Debug { get; set; }

    // Items whose identity is the collection name and whose Path metadata is the directory (consumers write import "name:pkg").
    public ITaskItem[] Collections { get; set; } = [];

    public string ExtraArguments { get; set; } = string.Empty;

    public override bool Execute()
    {
        string? odin = ResolveOdin();
        if (odin is null)
        {
            return false;
        }

        OdinBuildOptions options = new(
            PackageDirectory,
            OutputPath,
            Debug,
            Collections.Select(c => new KeyValuePair<string, string>(c.ItemSpec, c.GetMetadata("Path"))),
            ExtraArguments);
        string arguments = OdinBuildArguments.Build(options);
        Log.LogMessage(MessageImportance.High, "ODIN: {0} {1}", odin, arguments);

        string stdout;
        string stderr;
        int exitCode;
        try
        {
            exitCode = Run(odin, arguments, out stdout, out stderr);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.LogError(null, FailedWithoutDiagnosticsCode, null, PackageDirectory, 0, 0, 0, 0, "could not start '{0}': {1}. Check that odin.exe is a runnable executable.", odin, ex.Message);
            return false;
        }

        bool cancelled;
        lock (m_Lock)
        {
            cancelled = m_Cancelled;
        }

        if (cancelled)
        {
            // The killed odin exits non-zero with no error list; report nothing so the cancel does not look like a build failure.
            return false;
        }

        if (stdout.Trim().Length > 0)
        {
            Log.LogMessage(MessageImportance.Normal, "{0}", stdout.Trim());
        }

        OdinErrorOutput output = OdinErrorParser.Parse(stderr);
        foreach (OdinDiagnostic diagnostic in output.Diagnostics)
        {
            if (diagnostic.IsWarning)
            {
                Log.LogWarning(null, WarningCode, null, diagnostic.File, diagnostic.Line, diagnostic.Column, diagnostic.Line, diagnostic.EndColumn, "{0}", diagnostic.Message);
            }
            else
            {
                Log.LogError(null, ErrorCode, null, diagnostic.File, diagnostic.Line, diagnostic.Column, diagnostic.Line, diagnostic.EndColumn, "{0}", diagnostic.Message);
            }
        }

        bool failedWithoutDiagnostics = exitCode != 0 && !output.Diagnostics.Any(d => !d.IsWarning);
        if (output.OtherText.Length > 0 && !failedWithoutDiagnostics)
        {
            Log.LogMessage(MessageImportance.High, "{0}", output.OtherText);
        }

        if (failedWithoutDiagnostics)
        {
            // Failures with no JSON errors (linker, crash): do not report success; show whatever odin printed.
            string detail = output.OtherText.Length > 0 ? output.OtherText : "no output";
            Log.LogError(null, FailedWithoutDiagnosticsCode, null, PackageDirectory, 0, 0, 0, 0, "odin build failed with exit code {0} and no error list: {1}", exitCode, detail);
        }

        return !Log.HasLoggedErrors;
    }

    private string? ResolveOdin()
    {
        string? odin;
        string expectation;
        if (OdinExe.Length > 0)
        {
            odin = File.Exists(OdinExe) ? OdinExe : null;
            expectation = OdinExe;
        }
        else
        {
            ToolLocator locator = new(File.Exists);
            odin = locator.Find("odin.exe", (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator), [ToolResolver.OdinFallback]);
            expectation = $"PATH or {ToolResolver.OdinFallback}";
        }

        if (odin is null)
        {
            Log.LogError(null, OdinNotFoundCode, null, string.Empty, 0, 0, 0, 0, "odin.exe not found at {0}. Install Odin there, add its folder to PATH, or set the OdinExe property in the .odinproj.", expectation);
        }

        return odin;
    }

    // Called by MSBuild when the build is cancelled (Cancel Build in VS, Ctrl+C).
    public void Cancel()
    {
        lock (m_Lock)
        {
            m_Cancelled = true;
            if (m_Process is not null)
            {
                KillProcessTree(m_Process);
            }
        }
    }

    // Kills odin and the processes it started, using `taskkill /T`. Process.Kill() would only kill odin: its linker child inherits
    // our stdout/stderr pipes, so it would keep running and keep the pipes open, and ReadToEnd in Run would block until the link
    // finished. Process.Kill(entireProcessTree: true) does not exist on net472/netstandard2.0, and a job object is far more code for
    // the same result, so we call the Windows tool that is always present.
    private static void KillProcessTree(Process process)
    {
        try
        {
            ProcessStartInfo startInfo = new("taskkill", $"/T /F /PID {process.Id}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using Process? killer = Process.Start(startInfo);
            killer?.WaitForExit(10000);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // odin already exited, or taskkill could not run; the build is being cancelled either way.
        }
    }

    private int Run(string odin, string arguments, out string stdout, out string stderr)
    {
        // Odin writes UTF-8; the default console code page would mangle non-ASCII paths and messages.
        UTF8Encoding utf8 = new(false);
        ProcessStartInfo startInfo = new(odin, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8,
        };
        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Process.Start returned null");
        lock (m_Lock)
        {
            m_Process = process;
            if (m_Cancelled)
            {
                KillProcessTree(process);
            }
        }

        // Read both pipes at once so a full stderr buffer cannot stall odin.
        System.Threading.Tasks.Task<string> errors = process.StandardError.ReadToEndAsync();
        stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        stderr = errors.Result;
        lock (m_Lock)
        {
            m_Process = null;
        }

        return process.ExitCode;
    }
}
