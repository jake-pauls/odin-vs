using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace OdinVS.Core;

// Public because MSBuild instantiates tasks by reflection. The graph walk itself is OdinReferenceResolver (unit-tested).
// Nodes come from OdinWalkReferences (OdinGetNodeInfo of every project in the graph): identity = project path, metadata as read below.
public sealed class OdinResolveReferences : Microsoft.Build.Utilities.Task
{
    [Required]
    public string ProjectPath { get; set; } = string.Empty;

    [Required]
    public ITaskItem[] Nodes { get; set; } = [];

    // Same shape OdinBuild reads: identity = collection name, Path = root directory.
    [Output]
    public ITaskItem[] Collections { get; set; } = [];

    [Output]
    public ITaskItem[] LinkInputs { get; set; } = [];

    [Output]
    public ITaskItem[] Sources { get; set; } = [];

    public override bool Execute()
    {
        OdinResolvedReferences resolved = OdinReferenceResolver.Resolve(ProjectPath, Nodes.Select(ToNode));
        foreach (OdinReferenceProblem problem in resolved.Problems)
        {
            Log.LogError(null, problem.Code, null, ProjectPath, 0, 0, 0, 0, "{0}", problem.Message);
        }

        Collections = resolved.Collections.Select(c => (ITaskItem)new TaskItem(c.Key, new Dictionary<string, string> { ["Path"] = c.Value })).ToArray();
        LinkInputs = resolved.LinkInputs.Select(path => (ITaskItem)new TaskItem(path)).ToArray();
        Sources = resolved.Sources.Select(path => (ITaskItem)new TaskItem(path)).ToArray();
        return !Log.HasLoggedErrors;
    }

    private static OdinProjectNode ToNode(ITaskItem item)
    {
        string[] edges = item.GetMetadata("References").Split([';'], StringSplitOptions.RemoveEmptyEntries);
        List<OdinReferenceEdge> references = [];
        foreach (string edge in edges)
        {
            int bar = edge.IndexOf('|');
            OdinReferenceKind kind = bar > 0 && string.Equals(edge.Substring(0, bar), "Link", StringComparison.OrdinalIgnoreCase)
                ? OdinReferenceKind.Link
                : OdinReferenceKind.Source;
            references.Add(new OdinReferenceEdge(edge.Substring(bar + 1), kind));
        }

        return new OdinProjectNode(
            item.ItemSpec,
            item.GetMetadata("Name"),
            item.GetMetadata("CollectionName"),
            item.GetMetadata("CollectionRoot"),
            item.GetMetadata("LinkLibrary"),
            string.Equals(item.GetMetadata("IsNative"), "true", StringComparison.OrdinalIgnoreCase),
            references,
            item.GetMetadata("Sources").Split([';'], StringSplitOptions.RemoveEmptyEntries));
    }
}
