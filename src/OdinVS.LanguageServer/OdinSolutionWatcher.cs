using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using OdinVS.Core;

namespace OdinVS.LanguageServer;

// Tells the language client when the set of collections in the solution changes (a project added, removed, loaded or unloaded) so
// ols can be restarted with the new set, and keeps the latest collection problems (name collisions) for the info bar.
// Public because the package (another assembly) reads Problems and subscribes to ProblemsChanged.
public sealed class OdinSolutionWatcher : IVsSolutionEvents, IVsSolutionLoadEvents
{
    private const string ProjectExtension = ".odinproj";
    private const int DebounceMilliseconds = 500;

    public static OdinSolutionWatcher Instance { get; } = new();

    private readonly object m_Gate = new();
    private string m_Signature = string.Empty;
    private int m_Pending;
    private uint m_Cookie;

    public IReadOnlyList<OdinReferenceProblem> Problems { get; private set; } = [];

    // Raised on a threadpool thread when the collection set changed; the language client restarts ols.
    public event Action? CollectionsChanged;

    // Raised on a threadpool thread when the problems list changed.
    public event Action? ProblemsChanged;

    // Must run on the UI thread. Safe to call more than once.
    public void Start()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (m_Cookie != 0 || Package.GetGlobalService(typeof(SVsSolution)) is not IVsSolution solution)
        {
            return;
        }

        solution.AdviseSolutionEvents(this, out m_Cookie);
    }

    // The collections for the projects now in the solution. Runs the UI-thread part, then the file reads off it.
    public async System.Threading.Tasks.Task<OdinSolutionCollectionsResult> ComputeAsync(CancellationToken cancellationToken)
    {
        string[] files = await ProjectFilesAsync(cancellationToken);
        OdinSolutionCollectionsResult result = await System.Threading.Tasks.Task.Run(() => OdinSolutionCollections.Compute(files), cancellationToken);
        Publish(result);
        return result;
    }

    private static async System.Threading.Tasks.Task<string[]> ProjectFilesAsync(CancellationToken cancellationToken)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        if (Package.GetGlobalService(typeof(SVsSolution)) is not IVsSolution solution)
        {
            return [];
        }

        // Two calls: the first returns the count, the second fills the array. Unloaded projects have no file here.
        if (ErrorHandler.Failed(solution.GetProjectFilesInSolution((uint)__VSGETPROJFILESFLAGS.GPFF_SKIPUNLOADEDPROJECTS, 0, null, out uint count)) || count == 0)
        {
            return [];
        }

        string[] files = new string[count];
        if (ErrorHandler.Failed(solution.GetProjectFilesInSolution((uint)__VSGETPROJFILESFLAGS.GPFF_SKIPUNLOADEDPROJECTS, count, files, out _)))
        {
            return [];
        }

        return files.Where(f => !string.IsNullOrEmpty(f) && f.EndsWith(ProjectExtension, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private void Publish(OdinSolutionCollectionsResult result)
    {
        string signature = string.Join("\n", result.Collections.Select(c => c.Key + "=" + c.Value));
        bool changed;
        bool problemsChanged;
        lock (m_Gate)
        {
            changed = !string.Equals(signature, m_Signature, StringComparison.Ordinal);
            m_Signature = signature;
            string before = string.Join("\n", Problems.Select(p => p.Code + p.Message));
            problemsChanged = !string.Equals(before, string.Join("\n", result.Problems.Select(p => p.Code + p.Message)), StringComparison.Ordinal);
            Problems = result.Problems;
        }

        if (problemsChanged)
        {
            ProblemsChanged?.Invoke();
        }

        if (changed)
        {
            CollectionsChanged?.Invoke();
        }
    }

    // Remember what ols was started with, so a later recompute that yields the same set does not restart it.
    internal void MarkStarted(OdinSolutionCollectionsResult result)
    {
        lock (m_Gate)
        {
            m_Signature = string.Join("\n", result.Collections.Select(c => c.Key + "=" + c.Value));
        }
    }

    private void Schedule()
    {
        if (System.Threading.Interlocked.Exchange(ref m_Pending, 1) is 1)
        {
            return;
        }

        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(DebounceMilliseconds);
                System.Threading.Interlocked.Exchange(ref m_Pending, 0);
                await ComputeAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                System.Threading.Interlocked.Exchange(ref m_Pending, 0);
                Log.Default.Write($"could not refresh the solution's Odin collections: {ex}. IntelliSense keeps the previous set; reopen the solution to retry.");
            }
        });
    }

    public int OnAfterOpenProject(IVsHierarchy pHierarchy, int fAdded)
    {
        Schedule();
        return VSConstants.S_OK;
    }

    public int OnQueryCloseProject(IVsHierarchy pHierarchy, int fRemoving, ref int pfCancel) => VSConstants.S_OK;

    public int OnBeforeCloseProject(IVsHierarchy pHierarchy, int fRemoved)
    {
        Schedule();
        return VSConstants.S_OK;
    }

    public int OnAfterLoadProject(IVsHierarchy pStubHierarchy, IVsHierarchy pRealHierarchy)
    {
        Schedule();
        return VSConstants.S_OK;
    }

    public int OnQueryUnloadProject(IVsHierarchy pRealHierarchy, ref int pfCancel) => VSConstants.S_OK;

    public int OnBeforeUnloadProject(IVsHierarchy pRealHierarchy, IVsHierarchy pStubHierarchy)
    {
        Schedule();
        return VSConstants.S_OK;
    }

    public int OnAfterOpenSolution(object pUnkReserved, int fNewSolution)
    {
        Schedule();
        return VSConstants.S_OK;
    }

    public int OnQueryCloseSolution(object pUnkReserved, ref int pfCancel) => VSConstants.S_OK;

    public int OnBeforeCloseSolution(object pUnkReserved) => VSConstants.S_OK;

    public int OnAfterCloseSolution(object pUnkReserved)
    {
        Schedule();
        return VSConstants.S_OK;
    }

    public int OnBeforeOpeningChildren(IVsHierarchy pHierarchy) => VSConstants.S_OK;

    public int OnAfterOpeningChildren(IVsHierarchy pHierarchy) => VSConstants.S_OK;

    public int OnBeforeClosingChildren(IVsHierarchy pHierarchy) => VSConstants.S_OK;

    public int OnAfterClosingChildren(IVsHierarchy pHierarchy) => VSConstants.S_OK;

    public int OnBeforeOpenSolution(string pszSolutionFilename) => VSConstants.S_OK;

    public int OnBeforeBackgroundSolutionLoadBegins() => VSConstants.S_OK;

    public int OnQueryBackgroundLoadProjectBatch(out bool pfShouldDelayLoadToNextIdle)
    {
        pfShouldDelayLoadToNextIdle = false;
        return VSConstants.S_OK;
    }

    public int OnBeforeLoadProjectBatch(bool fIsBackgroundIdleBatch) => VSConstants.S_OK;

    public int OnAfterLoadProjectBatch(bool fIsBackgroundIdleBatch)
    {
        Schedule();
        return VSConstants.S_OK;
    }

    public int OnAfterBackgroundSolutionLoadComplete()
    {
        Schedule();
        return VSConstants.S_OK;
    }
}
