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
        OdinCollectionRegistry.Instance.Remove(m_Project.FullPath);
        m_Log.Write($"collection of {m_Project.FullPath} released");
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

            m_Log.Write($"collection of {m_Project.FullPath}: {name} = {root}");
            OdinCollectionRegistry.Instance.Set(m_Project.FullPath, name, root);
        }
        catch (Exception ex)
        {
            m_Log.Write($"could not read the collection of {m_Project.FullPath}: {ex}. IntelliSense may lack this project's collection; reopen the solution to retry.");
        }
    }
}
