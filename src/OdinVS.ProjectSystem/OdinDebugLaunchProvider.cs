using System.ComponentModel.Composition;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Debug;
using Microsoft.VisualStudio.ProjectSystem.Properties;
using Microsoft.VisualStudio.ProjectSystem.VS.Debug;
using OdinVS.Core;
using OdinVS.LanguageServer;

namespace OdinVS.ProjectSystem;

// Public because CPS discovers it through MEF. F5 and Ctrl+F5 both land here; launchOptions carries the NoDebug flag for
// Ctrl+F5. The name must equal the DebuggerFlavor default in Odin.Sdk's Sdk.targets.
[ExportDebugger("OdinDebugger")]
[AppliesTo("OdinProject")]
public sealed class OdinDebugLaunchProvider : DebugLaunchProviderBase
{
    // Native (and mixed-mode capable) Windows debug engine; value from VS's debug engine registry.
    private static readonly Guid s_NativeEngine = new("3B476D35-A401-11D2-AAD4-00C04F990171");

    private readonly ConfiguredProject m_ConfiguredProject;
    private readonly Log m_Log = Log.Default;

    [ImportingConstructor]
    public OdinDebugLaunchProvider(ConfiguredProject configuredProject)
        : base(configuredProject)
    {
        m_ConfiguredProject = configuredProject;
    }

    private async Task<OdinLaunchSettings> ResolveAsync()
    {
        IProjectProperties properties = m_ConfiguredProject.Services.ProjectPropertiesProvider!.GetCommonProperties();
        string projectDirectory = Path.GetDirectoryName(m_ConfiguredProject.UnconfiguredProject.FullPath)!;
        return OdinLaunchSettings.Resolve(
            await properties.GetEvaluatedPropertyValueAsync("TargetPath"),
            await properties.GetEvaluatedPropertyValueAsync("LocalDebuggerWorkingDirectory"),
            await properties.GetEvaluatedPropertyValueAsync("LocalDebuggerCommandArguments"),
            projectDirectory);
    }

    public override async Task<bool> CanLaunchAsync(DebugLaunchOptions launchOptions)
    {
        try
        {
            OdinLaunchSettings settings = await ResolveAsync();
            bool exists = File.Exists(settings.Executable);
            if (!exists)
            {
                m_Log.Write($"F5: {settings.Executable} does not exist; build the project first (check its TargetPath if it builds elsewhere)");
            }

            return exists;
        }
        catch (InvalidOperationException ex)
        {
            m_Log.Write($"F5 not possible: {ex.Message}");
            return false;
        }
    }

    public override async Task<IReadOnlyList<IDebugLaunchSettings>> QueryDebugTargetsAsync(DebugLaunchOptions launchOptions)
    {
        OdinLaunchSettings settings = await ResolveAsync();
        m_Log.Write($"launch options={launchOptions} exe={settings.Executable} args='{settings.Arguments}' cwd={settings.WorkingDirectory}");
        DebugLaunchSettings launch = new(launchOptions)
        {
            Executable = settings.Executable,
            Arguments = settings.Arguments,
            CurrentDirectory = settings.WorkingDirectory,
            LaunchOperation = DebugLaunchOperation.CreateProcess,
            LaunchDebugEngineGuid = s_NativeEngine,
            Project = VsHierarchy,
        };

        return [launch];
    }
}