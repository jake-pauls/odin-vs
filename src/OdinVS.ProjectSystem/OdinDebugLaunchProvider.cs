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

    // Like the C# project system (ProjectLaunchTargetsProvider): CanLaunchAsync always says yes, because VS has already built by
    // the time it asks, and QueryDebugTargetsAsync throws with a message naming the missing executable or directory, which VS
    // shows to the user. Refusing quietly would make F5 do nothing.
    public override Task<bool> CanLaunchAsync(DebugLaunchOptions launchOptions) => Task.FromResult(true);

    public override async Task<IReadOnlyList<IDebugLaunchSettings>> QueryDebugTargetsAsync(DebugLaunchOptions launchOptions)
    {
        string projectName = Path.GetFileNameWithoutExtension(m_ConfiguredProject.UnconfiguredProject.FullPath);
        try
        {
            IProjectProperties common = m_ConfiguredProject.Services.ProjectPropertiesProvider!.GetCommonProperties();
            string outputType = await common.GetEvaluatedPropertyValueAsync("OdinOutputType");
            if (outputType.Equals("Dll", StringComparison.OrdinalIgnoreCase) || outputType.Equals("Lib", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Project '{projectName}' builds a library (OdinOutputType is {outputType}), which cannot be started. Set an executable project that references it as the startup project.");
            }

            OdinLaunchSettings settings = await ResolveAsync();
            string? problem = settings.Validate(File.Exists, Directory.Exists, projectName);
            if (problem is not null)
            {
                throw new InvalidOperationException(problem);
            }

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
        catch (InvalidOperationException ex)
        {
            m_Log.Write($"F5 not possible: {ex.Message}");
            throw;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // A path typed on the Debug page that Windows cannot parse.
            string message = $"The launch settings of project '{projectName}' are not valid: {ex.Message} Check TargetPath and the LocalDebugger* properties in the .odinproj.";
            m_Log.Write($"F5 not possible: {message}");
            throw new InvalidOperationException(message, ex);
        }
    }
}