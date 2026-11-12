using OdinVS.Core;

namespace OdinVS.LanguageServer;

// The evaluated collection of every loaded .odinproj, fed by the project system (one entry per project, replaced whenever CPS
// re-evaluates it and removed when it unloads). Changes are debounced, then the union is recomputed; when it differs the language
// client restarts ols, and when the collision list differs the package refreshes its info bar.
// Public because the project system (another assembly) writes to it and the package reads Problems.
public sealed class OdinCollectionRegistry
{
    private const int DebounceMilliseconds = 500;

    public static OdinCollectionRegistry Instance { get; } = new();

    private readonly object m_Gate = new();
    private readonly Dictionary<string, OdinProjectCollection> m_Projects = new(StringComparer.OrdinalIgnoreCase);
    private string m_Signature = string.Empty;
    private string m_ProblemSignature = string.Empty;
    private int m_Pending;

    public IReadOnlyList<OdinReferenceProblem> Problems { get; private set; } = [];

    // Raised on a threadpool thread when the collection set changed.
    public event Action? CollectionsChanged;

    // Raised on a threadpool thread when the collision list changed.
    public event Action? ProblemsChanged;

    public void Set(string projectFile, string name, string root)
    {
        lock (m_Gate)
        {
            m_Projects[projectFile] = new OdinProjectCollection(projectFile, name, root);
        }

        Schedule();
    }

    public void Remove(string projectFile)
    {
        lock (m_Gate)
        {
            m_Projects.Remove(projectFile);
        }

        Schedule();
    }

    // The collections for the projects loaded now. The language client calls this when it starts ols and remembers the result, so a
    // later change that yields the same set does not restart it.
    public OdinSolutionCollectionsResult Current()
    {
        OdinSolutionCollectionsResult result;
        lock (m_Gate)
        {
            result = OdinSolutionCollections.Compute(m_Projects.Values);
            m_Signature = Signature(result);
        }

        return result;
    }

    private void Schedule()
    {
        if (Interlocked.Exchange(ref m_Pending, 1) is 1)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DebounceMilliseconds);
                Interlocked.Exchange(ref m_Pending, 0);
                Publish();
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref m_Pending, 0);
                Log.Default.Write($"could not refresh the solution's Odin collections: {ex}. IntelliSense keeps the previous set; reopen the solution to retry.");
            }
        });
    }

    private void Publish()
    {
        bool changed;
        bool problemsChanged;
        lock (m_Gate)
        {
            OdinSolutionCollectionsResult result = OdinSolutionCollections.Compute(m_Projects.Values);
            string signature = Signature(result);
            changed = !string.Equals(signature, m_Signature, StringComparison.Ordinal);
            m_Signature = signature;
            string problemSignature = string.Join("\n", result.Problems.Select(p => p.Code + p.Message));
            problemsChanged = !string.Equals(problemSignature, m_ProblemSignature, StringComparison.Ordinal);
            m_ProblemSignature = problemSignature;
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

    private static string Signature(OdinSolutionCollectionsResult result) =>
        string.Join("\n", result.Collections.Select(c => c.Key + "=" + c.Value));
}
