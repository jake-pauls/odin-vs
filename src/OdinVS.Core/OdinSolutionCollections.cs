using System.Xml.Linq;

namespace OdinVS.Core;

public sealed record OdinSolutionCollectionsResult(
    IReadOnlyList<KeyValuePair<string, string>> Collections,
    IReadOnlyList<OdinReferenceProblem> Problems);

// The collections the language server is given for a whole solution: one per .odinproj, whether or not another project references
// it, so IntelliSense works for every `import "name:pkg"` in the solution. ols takes one collection set per process
// (OlsCollectionTests), so this is the union; build-time collections still come from the reference graph.
// Reads the project XML directly (no MSBuild evaluation): a collection name or root that is not a literal, or a literal with
// $(MSBuildProjectName)/$(MSBuildProjectDirectory), is reported (ODIN0108) and the project's default is used.
public static class OdinSolutionCollections
{
    public const string DuplicateCode = "ODIN0103";
    public const string UnreadableCode = "ODIN0108";

    public static OdinSolutionCollectionsResult Compute(IEnumerable<string> projectFiles)
    {
        List<KeyValuePair<string, string>> collections = [];
        List<OdinReferenceProblem> problems = [];
        Dictionary<string, string> owners = new(StringComparer.Ordinal);
        foreach (string file in projectFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            (string name, string root) = Read(file, problems);
            if (owners.TryGetValue(name, out string? owner))
            {
                if (!string.Equals(owner, root, StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add(new OdinReferenceProblem(DuplicateCode, $"collection name '{name}' is used by two projects ('{owner}' and '{root}'). Give each project a different OdinCollectionName (the default is the project name), or remove the OdinCollectionName that makes them match."));
                }

                continue;
            }

            owners[name] = root;
            collections.Add(new KeyValuePair<string, string>(name, root));
        }

        return new OdinSolutionCollectionsResult(collections, problems);
    }

    private static (string Name, string Root) Read(string file, List<OdinReferenceProblem> problems)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(file))!;
        string projectName = Path.GetFileNameWithoutExtension(file);
        string name = projectName;
        string root = directory;
        try
        {
            XDocument document = XDocument.Load(file);
            string? declaredName = Property(document, "OdinCollectionName");
            string? declaredRoot = Property(document, "OdinCollectionRoot");
            if (declaredName is not null)
            {
                name = Substitute(declaredName, projectName, directory, file, "OdinCollectionName", problems) ?? name;
            }

            if (declaredRoot is not null)
            {
                string? value = Substitute(declaredRoot, projectName, directory, file, "OdinCollectionRoot", problems);
                if (value is not null)
                {
                    root = Path.GetFullPath(Path.Combine(directory, value));
                }
            }
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException or UnauthorizedAccessException)
        {
            problems.Add(new OdinReferenceProblem(UnreadableCode, $"could not read '{file}' for its collection: {ex.Message}. The default (project name, project folder) is used."));
        }

        return (name, root.TrimEnd('\\', '/'));
    }

    // The last unconditional value wins, like MSBuild evaluation order within one file.
    private static string? Property(XDocument document, string name)
    {
        return document.Descendants()
            .Where(e => e.Name.LocalName == name && e.Parent?.Name.LocalName == "PropertyGroup"
                && e.Parent.Attribute("Condition") is null && e.Attribute("Condition") is null)
            .Select(e => e.Value.Trim())
            .LastOrDefault(v => v.Length > 0);
    }

    private static string? Substitute(string value, string projectName, string directory, string file, string property, List<OdinReferenceProblem> problems)
    {
        string result = value.Replace("$(MSBuildProjectName)", projectName).Replace("$(MSBuildProjectDirectory)", directory);
        if (result.Contains("$("))
        {
            problems.Add(new OdinReferenceProblem(UnreadableCode, $"{property} in '{file}' uses a property the language server cannot evaluate ('{value}'). The default is used for IntelliSense; builds are not affected."));
            return null;
        }

        return result;
    }
}
