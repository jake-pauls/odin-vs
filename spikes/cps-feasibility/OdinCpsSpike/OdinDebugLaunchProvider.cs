using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Debug;
using Microsoft.VisualStudio.ProjectSystem.VS.Debug;

namespace OdinCpsSpike;

/// <summary>
/// B5 spike: F5 / Ctrl+F5 for .odinproj. Launches the exe produced by the project's Build target under the native engine.
/// The output path is hard-coded to the spike layout (bin\&lt;Configuration&gt;\&lt;ProjectName&gt;.exe); the real project
/// would read TargetPath from the evaluated project.
/// </summary>
[ExportDebugger("OdinDebugger")]
[AppliesTo("OdinProject")]
internal sealed class OdinDebugLaunchProvider : DebugLaunchProviderBase
{
    // Native (and mixed-mode capable) Windows debug engine; value from VS's debug engine registry.
    private static readonly Guid NativeEngine = new Guid("3B476D35-A401-11D2-AAD4-00C04F990171");

    private readonly ConfiguredProject _configuredProject;

    [ImportingConstructor]
    public OdinDebugLaunchProvider(ConfiguredProject configuredProject)
        : base(configuredProject)
    {
        _configuredProject = configuredProject;
    }

    private string GetExecutablePath()
    {
        string projectPath = _configuredProject.UnconfiguredProject.FullPath;
        string projectDir = Path.GetDirectoryName(projectPath)!;
        string configuration = _configuredProject.ProjectConfiguration.Dimensions.TryGetValue("Configuration", out string? value) ? value : "Debug";
        return Path.Combine(projectDir, "bin", configuration, Path.GetFileNameWithoutExtension(projectPath) + ".exe");
    }

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "odin-debug.log"), DateTime.Now.ToString("O") + " " + message + Environment.NewLine);
        }
        catch (IOException)
        {
        }
    }

    public override Task<bool> CanLaunchAsync(DebugLaunchOptions launchOptions)
    {
        string exe = GetExecutablePath();
        bool exists = File.Exists(exe);
        Log($"CanLaunchAsync options={launchOptions} exe={exe} exists={exists}");
        return Task.FromResult(exists);
    }

    public override Task<IReadOnlyList<IDebugLaunchSettings>> QueryDebugTargetsAsync(DebugLaunchOptions launchOptions)
    {
        string exe = GetExecutablePath();
        Log($"QueryDebugTargetsAsync options={launchOptions} exe={exe}");
        DebugLaunchSettings settings = new DebugLaunchSettings(launchOptions)
        {
            Executable = exe,
            Arguments = string.Empty,
            CurrentDirectory = Path.GetDirectoryName(_configuredProject.UnconfiguredProject.FullPath),
            LaunchOperation = DebugLaunchOperation.CreateProcess,
            LaunchDebugEngineGuid = NativeEngine,
            Project = VsHierarchy,
        };

        IReadOnlyList<IDebugLaunchSettings> result = [settings];
        return Task.FromResult(result);
    }
}
