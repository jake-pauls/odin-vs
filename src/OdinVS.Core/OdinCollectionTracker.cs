namespace OdinVS.Core;

// The evaluated collection of every loaded .odinproj, fed by the project system (one entry per project, replaced whenever CPS
// re-evaluates it and removed when it unloads). Changes are debounced: the union is recomputed once no change has arrived for
// `quiet`, or after `maxWait` of continuous changes (a big solution loads for seconds, and every project reports in). When the union
// differs from the last one handed out (Current or an earlier publish) CollectionsChanged is raised, and ProblemsChanged when the
// collision list differs. Both are raised on a threadpool thread.
public sealed class OdinCollectionTracker(TimeSpan quiet, TimeSpan maxWait, Action<string> log)
{
    private readonly object m_Gate = new();
    private readonly Dictionary<string, OdinProjectCollection> m_Projects = new(StringComparer.OrdinalIgnoreCase);
    private string m_Signature = string.Empty;
    private string m_ProblemSignature = string.Empty;
    private bool m_Pending;
    private DateTime m_FirstChange;
    private DateTime m_LastChange;

    public IReadOnlyList<OdinReferenceProblem> Problems { get; private set; } = [];

    public event Action? CollectionsChanged;

    public event Action? ProblemsChanged;

    public void Set(string projectFile, string name, string root)
    {
        lock (m_Gate)
        {
            m_Projects[projectFile] = new OdinProjectCollection(projectFile, name, root);
        }

        Touch();
    }

    public void Remove(string projectFile)
    {
        lock (m_Gate)
        {
            m_Projects.Remove(projectFile);
        }

        Touch();
    }

    // The union for the projects loaded now. The language client calls this when it starts ols and keeps the result, so a later
    // change that yields the same set does not restart it.
    public OdinSolutionCollectionsResult Current()
    {
        lock (m_Gate)
        {
            OdinSolutionCollectionsResult result = OdinSolutionCollections.Compute(m_Projects.Values);
            m_Signature = Signature(result);
            return result;
        }
    }

    private void Touch()
    {
        lock (m_Gate)
        {
            m_LastChange = DateTime.UtcNow;
            if (m_Pending)
            {
                return;
            }

            m_Pending = true;
            m_FirstChange = m_LastChange;
        }

        _ = Task.Run(WaitAndPublishAsync);
    }

    private async Task WaitAndPublishAsync()
    {
        try
        {
            while (true)
            {
                TimeSpan wait;
                lock (m_Gate)
                {
                    DateTime now = DateTime.UtcNow;
                    TimeSpan sinceLast = now - m_LastChange;
                    TimeSpan sinceFirst = now - m_FirstChange;
                    if (sinceLast >= quiet || sinceFirst >= maxWait)
                    {
                        m_Pending = false;
                        break;
                    }

                    TimeSpan untilQuiet = quiet - sinceLast;
                    TimeSpan untilCap = maxWait - sinceFirst;
                    wait = untilQuiet < untilCap ? untilQuiet : untilCap;
                }

                await Task.Delay(wait);
            }

            Publish();
        }
        catch (Exception ex)
        {
            lock (m_Gate)
            {
                m_Pending = false;
            }

            log($"could not refresh the solution's Odin collections: {ex}. IntelliSense keeps the previous set; reopen the solution to retry.");
        }
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
