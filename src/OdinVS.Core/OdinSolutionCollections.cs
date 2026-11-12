namespace OdinVS.Core;

// One project's evaluated collection (name and root come from MSBuild through the project system, so imports, conditions and
// $(...) values are already resolved).
public sealed record OdinProjectCollection(string ProjectFile, string Name, string Root);

public sealed record OdinSolutionCollectionsResult(
    IReadOnlyList<KeyValuePair<string, string>> Collections,
    IReadOnlyList<OdinReferenceProblem> Problems);

// The collections the language server is given for a whole solution: one per .odinproj, whether or not another project references
// it, so IntelliSense works for every `import "name:pkg"` in the solution. ols takes one collection set per process
// (OlsCollectionTests), so this is the union; build-time collections still come from the reference graph.
public static class OdinSolutionCollections
{
    public const string DuplicateCode = "ODIN0103";

    // Sorted by project path so the first project keeps a contested name, the same way on every run.
    public static OdinSolutionCollectionsResult Compute(IEnumerable<OdinProjectCollection> projects)
    {
        List<KeyValuePair<string, string>> collections = [];
        List<OdinReferenceProblem> problems = [];
        Dictionary<string, string> owners = new(StringComparer.Ordinal);
        foreach (OdinProjectCollection project in projects.OrderBy(p => p.ProjectFile, StringComparer.OrdinalIgnoreCase))
        {
            string root = project.Root.TrimEnd('\\', '/');
            if (owners.TryGetValue(project.Name, out string? owner))
            {
                if (!string.Equals(owner, root, StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add(new OdinReferenceProblem(DuplicateCode, $"collection name '{project.Name}' is used by two projects ('{owner}' and '{root}'). Give each project a different OdinCollectionName (the default is the project name), or remove the OdinCollectionName that makes them match."));
                }

                continue;
            }

            owners[project.Name] = root;
            collections.Add(new KeyValuePair<string, string>(project.Name, root));
        }

        return new OdinSolutionCollectionsResult(collections, problems);
    }
}
