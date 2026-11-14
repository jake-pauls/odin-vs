using System.ComponentModel.Composition;
using System.Threading.Tasks.Dataflow;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Properties;
using OdinVS.LanguageServer;

namespace OdinVS.ProjectSystem;

// Public because CPS discovers it through MEF. Follows the evaluated OdinCollectionName/OdinCollectionRoot of the project (rule
// OdinCollections in Odin.Sdk), so the language server sees what MSBuild computed: imports, conditions and $(...) values included,
// and a changed .odinproj is picked up without reopening the solution. Entries leave the registry when the project unloads.
[Export(ExportContractNames.Scopes.UnconfiguredProject, typeof(IProjectDynamicLoadComponent))]
[AppliesTo("OdinProject")]
public sealed class OdinCollectionSubscriber : IProjectDynamicLoadComponent
{
    private const string RuleName = "OdinCollections";
    private const string NameProperty = "OdinCollectionName";
    private const string RootProperty = "OdinCollectionRoot";

    private readonly UnconfiguredProject m_Project;
    private readonly IActiveConfiguredProjectSubscriptionService m_Subscription;
    private readonly Log m_Log = Log.Default;
    private IDisposable? m_Link;
    private string m_LastLogged = string.Empty;
    private volatile string? m_RegisteredPath;

    [ImportingConstructor]
    public OdinCollectionSubscriber(UnconfiguredProject project, IActiveConfiguredProjectSubscriptionService subscription)
    {
        m_Project = project;
        m_Subscription = subscription;
    }

    [ProjectAutoLoad(ProjectLoadCheckpoint.ProjectFactoryCompleted, ProjectLoadCheckpoint.ProjectUpdated)]
    public Task LoadAsync()
    {
        m_Log.Write($"collection subscriber loaded for {m_Project.FullPath}");
        // UnloadAsync is not called when a project is removed from the solution (seen in Exp), the project's own event is.
        m_Project.ProjectUnloading += OnProjectUnloadingAsync;
        ActionBlock<IProjectVersionedValue<IProjectSubscriptionUpdate>> block = new(OnUpdate);
        // Without the rule name CPS leaves the rule out of the snapshots (only rules somebody asked for are computed).
        m_Link = m_Subscription.ProjectRuleSource.SourceBlock.LinkTo(block, new DataflowLinkOptions { PropagateCompletion = true }, ruleNames: (IEnumerable<string>)[RuleName]);
        return Task.CompletedTask;
    }

    public Task UnloadAsync()
    {
        Release();
        return Task.CompletedTask;
    }

    private Task OnProjectUnloadingAsync(object? sender, EventArgs e)
    {
        Release();
        return Task.CompletedTask;
    }

    private void Release()
    {
        m_Project.ProjectUnloading -= OnProjectUnloadingAsync;
        m_Link?.Dispose();
        m_Link = null;
        // The path the entry was registered under, not FullPath now: a renamed project reports its new path here.
        string path = m_RegisteredPath ?? m_Project.FullPath;
        OdinCollectionRegistry.Instance.Remove(path);
        m_RegisteredPath = null;
        m_Log.Write($"collection of {path} released");
    }

    // Runs on a dataflow thread; must not throw (an exception would fault the block and end the subscription).
    private void OnUpdate(IProjectVersionedValue<IProjectSubscriptionUpdate> update)
    {
        try
        {
            if (!update.Value.CurrentState.TryGetValue(RuleName, out IProjectRuleSnapshot? snapshot)
                || !snapshot.Properties.TryGetValue(NameProperty, out string? name)
                || !snapshot.Properties.TryGetValue(RootProperty, out string? root)
                || string.IsNullOrWhiteSpace(name)
                || string.IsNullOrWhiteSpace(root))
            {
                m_Log.Write($"collection update for {m_Project.FullPath} had no usable {RuleName} values; rules seen: {string.Join(", ", update.Value.CurrentState.Keys)}");
                return;
            }

            string value = name + " = " + root;
            if (!string.Equals(value, m_LastLogged, StringComparison.Ordinal))
            {
                m_LastLogged = value;
                m_Log.Write($"collection of {m_Project.FullPath}: {value}");
            }

            string path = m_Project.FullPath;
            if (m_RegisteredPath is not null && !string.Equals(m_RegisteredPath, path, StringComparison.OrdinalIgnoreCase))
            {
                // The project was renamed or moved: drop the entry under the old path, or it would stay until the solution is reopened.
                OdinCollectionRegistry.Instance.Remove(m_RegisteredPath);
            }

            m_RegisteredPath = path;
            OdinCollectionRegistry.Instance.Set(path, name, root);
        }
        catch (Exception ex)
        {
            m_Log.Write($"could not read the collection of {m_Project.FullPath}: {ex}. IntelliSense may lack this project's collection; reopen the solution to retry.");
        }
    }
}
