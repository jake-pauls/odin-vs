namespace OdinVS.Core;

public sealed class ToolLocator(Func<string, bool> fileExists)
{
    // Relative PATH entries (".", "bin") are skipped so a tool is never picked up from the current directory.
    public string? Find(string fileName, IEnumerable<string> searchDirectories, IEnumerable<string> fallbackPaths)
    {
        foreach (string directory in searchDirectories)
        {
            string? candidate = Combine(directory, fileName);
            if (candidate is not null && fileExists(candidate))
            {
                return candidate;
            }
        }

        foreach (string path in fallbackPaths)
        {
            if (fileExists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static string? Combine(string directory, string fileName)
    {
        string trimmed = directory.Trim().Trim('"');
        try
        {
            return trimmed.Length > 0 && Path.IsPathRooted(trimmed) ? Path.Combine(trimmed, fileName) : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
