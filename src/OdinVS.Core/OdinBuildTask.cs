using System.Diagnostics;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace OdinVS.Core;

// Public because MSBuild instantiates tasks by reflection. Argument building and error parsing live in internal classes with unit tests.
public sealed class OdinBuild : Microsoft.Build.Utilities.Task
{
    public const string ErrorCode = "ODIN0001";
    public const string WarningCode = "ODIN0002";
    public const string FailedWithoutDiagnosticsCode = "ODIN0100";
    public const string OdinNotFoundCode = "ODIN0101";

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

        if (output.OtherText.Length > 0)
        {
            Log.LogMessage(MessageImportance.High, "{0}", output.OtherText);
        }

        if (exitCode != 0 && !output.Diagnostics.Any(d => !d.IsWarning))
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

    private static int Run(string odin, string arguments, out string stdout, out string stderr)
    {
        ProcessStartInfo startInfo = new(odin, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Process.Start returned null");
        // Read both pipes at once so a full stderr buffer cannot stall odin.
        System.Threading.Tasks.Task<string> errors = process.StandardError.ReadToEndAsync();
        stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        stderr = errors.Result;
        return process.ExitCode;
    }
}
