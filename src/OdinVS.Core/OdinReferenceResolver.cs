namespace OdinVS.Core;

public enum OdinReferenceKind
{
    // The consumer compiles the referenced project's sources through -collection:name=root (Odin to Odin, the default).
    Source,

    // The consumer links the referenced project's library file (Odin to native, or Odin to an Odin library).
    Link,
}

public sealed record OdinReferenceEdge(string ProjectPath, OdinReferenceKind Kind);

// One project of the reference graph. Native projects (.vcxproj, .csproj) are leaves: only their LinkLibrary matters.
// Sources are the project's own OdinCompile files (consumers recompile them, so they are incremental-build inputs).
public sealed record OdinProjectNode(
    string ProjectPath,
    string Name,
    string CollectionName,
    string CollectionRoot,
    string LinkLibrary,
    bool IsNative,
    IReadOnlyList<OdinReferenceEdge> References,
    IReadOnlyList<string> Sources);

public sealed record OdinReferenceProblem(string Code, string Message);

public sealed record OdinResolvedReferences(
    IReadOnlyList<KeyValuePair<string, string>> Collections,
    IReadOnlyList<string> LinkInputs,
    IReadOnlyList<string> Sources,
    IReadOnlyList<OdinReferenceProblem> Problems);

// Computes what a project's `odin build` needs from its references: the transitive collections (Source edges), the link inputs
// (Link edges, found through Source edges as well) and the referenced sources. The authoritative cycle check lives here so command-line
// MSBuild and Visual Studio fail identically; MSBuild itself only orders the builds.
public static class OdinReferenceResolver
{
    public const string CycleCode = "ODIN0102";
    public const string DuplicateCollectionCode = "ODIN0103";
    public const string UnresolvedReferenceCode = "ODIN0104";
    public const string NoLibraryCode = "ODIN0105";

    public static OdinResolvedReferences Resolve(string rootProjectPath, IEnumerable<OdinProjectNode> nodes)
    {
        Dictionary<string, OdinProjectNode> byPath = new(StringComparer.OrdinalIgnoreCase);
        foreach (OdinProjectNode node in nodes)
        {
            byPath[node.ProjectPath] = node;
        }

        List<OdinReferenceProblem> problems = [];
        if (!byPath.ContainsKey(rootProjectPath))
        {
            problems.Add(new OdinReferenceProblem(UnresolvedReferenceCode, $"project '{rootProjectPath}' is missing from the reference graph."));
            return new OdinResolvedReferences([], [], [], problems);
        }

        Dictionary<string, int> state = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> reportedCycles = new(StringComparer.Ordinal);
        FindCycles(rootProjectPath, byPath, state, [], reportedCycles, problems);
        if (problems.Count > 0)
        {
            return new OdinResolvedReferences([], [], [], problems);
        }

        List<KeyValuePair<string, string>> collections = [];
        Dictionary<string, string> collectionOwners = new(StringComparer.Ordinal);
        List<string> linkInputs = [];
        List<string> sources = [];
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase) { rootProjectPath };
        Collect(byPath[rootProjectPath], byPath, visited, collections, collectionOwners, linkInputs, sources, problems);
        return new OdinResolvedReferences(collections, linkInputs, sources, problems);
    }

    // Depth-first search over every edge kind (a Link cycle would loop the solution build just as well). Each cycle is reported once.
    private static void FindCycles(
        string path,
        Dictionary<string, OdinProjectNode> byPath,
        Dictionary<string, int> state,
        List<string> stack,
        HashSet<string> reportedCycles,
        List<OdinReferenceProblem> problems)
    {
        const int InProgress = 1;
        const int Done = 2;
        state[path] = InProgress;
        stack.Add(path);
        foreach (OdinReferenceEdge edge in byPath[path].References)
        {
            if (!byPath.TryGetValue(edge.ProjectPath, out OdinProjectNode? target))
            {
                problems.Add(new OdinReferenceProblem(UnresolvedReferenceCode, $"'{byPath[path].Name}' references '{edge.ProjectPath}', which could not be read. Check that the project exists and loads."));
                continue;
            }

            int targetState = state.TryGetValue(target.ProjectPath, out int s) ? s : 0;
            if (targetState == InProgress)
            {
                int start = stack.FindIndex(p => string.Equals(p, target.ProjectPath, StringComparison.OrdinalIgnoreCase));
                IEnumerable<string> names = stack.Skip(start).Select(p => byPath[p].Name).Append(target.Name);
                string cycle = string.Join(" -> ", names);
                if (reportedCycles.Add(cycle))
                {
                    problems.Add(new OdinReferenceProblem(CycleCode, $"circular project reference: {cycle}. Remove one of these references."));
                }
            }
            else if (targetState == 0)
            {
                FindCycles(target.ProjectPath, byPath, state, stack, reportedCycles, problems);
            }
        }

        stack.RemoveAt(stack.Count - 1);
        state[path] = Done;
    }

    private static void Collect(
        OdinProjectNode node,
        Dictionary<string, OdinProjectNode> byPath,
        HashSet<string> visited,
        List<KeyValuePair<string, string>> collections,
        Dictionary<string, string> collectionOwners,
        List<string> linkInputs,
        List<string> sources,
        List<OdinReferenceProblem> problems)
    {
        foreach (OdinReferenceEdge edge in node.References)
        {
            OdinProjectNode target = byPath[edge.ProjectPath];
            bool asSource = edge.Kind is OdinReferenceKind.Source && !target.IsNative;
            if (!asSource)
            {
                if (target.LinkLibrary.Length == 0)
                {
                    problems.Add(new OdinReferenceProblem(NoLibraryCode, $"'{node.Name}' links '{target.Name}', but '{target.Name}' produces no library to link. Reference it as a source reference, or make it a library project."));
                }
                else if (!linkInputs.Contains(target.LinkLibrary, StringComparer.OrdinalIgnoreCase))
                {
                    linkInputs.Add(target.LinkLibrary);
                }

                continue;
            }

            if (collectionOwners.TryGetValue(target.CollectionName, out string? owner))
            {
                if (!string.Equals(owner, target.CollectionRoot, StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add(new OdinReferenceProblem(DuplicateCollectionCode, $"collection name '{target.CollectionName}' is used by two projects ('{owner}' and '{target.CollectionRoot}'). Set OdinCollectionName in one of them."));
                }
            }
            else
            {
                collectionOwners[target.CollectionName] = target.CollectionRoot;
                collections.Add(new KeyValuePair<string, string>(target.CollectionName, target.CollectionRoot));
            }

            if (visited.Add(target.ProjectPath))
            {
                sources.AddRange(target.Sources);
                Collect(target, byPath, visited, collections, collectionOwners, linkInputs, sources, problems);
            }
        }
    }
}
