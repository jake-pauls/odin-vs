using System.Collections;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace OdinVS.Core;

// Public because MSBuild instantiates tasks by reflection. Collects the reference graph one level at a time: each level asks the
// referenced projects for their own node (OdinGetNodeInfo, which does not recurse), so a cycle cannot trip MSBuild's circular-target
// guard (MSB4006) and OdinReferenceResolver reports it with its path (ODIN0102). Results are cached per project by MSBuild, so a
// diamond or a 100-project chain costs one evaluation per project.
public sealed class OdinWalkReferences : Microsoft.Build.Utilities.Task
{
    private const string NodeTarget = "OdinGetNodeInfo";

    [Required]
    public string ProjectPath { get; set; } = string.Empty;

    // This project's own nodes (itself plus its native references), from OdinGetNodeInfo.
    [Required]
    public ITaskItem[] Nodes { get; set; } = [];

    [Output]
    public ITaskItem[] AllNodes { get; set; } = [];

    public override bool Execute()
    {
        if (BuildEngine is not IBuildEngine3 engine)
        {
            Log.LogError(null, OdinDiagnosticCodes.HostCannotWalkReferences, null, ProjectPath, 0, 0, 0, 0, "{0}", "this MSBuild host cannot build referenced projects from a task (IBuildEngine3 is missing), so the reference graph cannot be read.");
            return false;
        }

        List<ITaskItem> all = [.. Nodes];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase) { ProjectPath };
        List<string> frontier = NextLevel(Nodes, seen);
        while (frontier.Count > 0)
        {
            // Empty global properties and no removals: the referenced projects inherit this project's (Configuration, Platform ...).
            IDictionary[] globals = frontier.Select(_ => (IDictionary)new Dictionary<string, string>()).ToArray();
            IList<string>[] removals = frontier.Select(_ => (IList<string>)new List<string>()).ToArray();
            BuildEngineResult result = engine.BuildProjectFilesInParallel(
                frontier.ToArray(),
                frontier.Select(_ => NodeTarget).ToArray(),
                globals,
                removals,
                frontier.Select(_ => (string)null!).ToArray(),
                true);
            if (!result.Result)
            {
                // The failing project has logged its own errors.
                return false;
            }

            List<ITaskItem> level = [];
            foreach (IDictionary<string, ITaskItem[]> outputs in result.TargetOutputsPerProject)
            {
                foreach (ITaskItem[] items in outputs.Values)
                {
                    level.AddRange(items);
                }
            }

            all.AddRange(level);
            frontier = NextLevel(level, seen);
        }

        AllNodes = all.ToArray();
        return true;
    }

    // Odin projects referenced by these nodes that were not walked yet (native projects are leaves).
    private static List<string> NextLevel(IEnumerable<ITaskItem> nodes, HashSet<string> seen)
    {
        List<string> next = [];
        foreach (ITaskItem node in nodes)
        {
            foreach (string edge in node.GetMetadata("References").Split([';'], StringSplitOptions.RemoveEmptyEntries))
            {
                string path = edge.Substring(edge.IndexOf('|') + 1);
                if (path.EndsWith(".odinproj", StringComparison.OrdinalIgnoreCase) && seen.Add(path))
                {
                    next.Add(path);
                }
            }
        }

        return next;
    }
}
