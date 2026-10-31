namespace OdinVS.Core;

internal sealed class ToolLocator(Func<string, bool> fileExists)
{
    public string? Find(string fileName, IEnumerable<string> searchDirectories, IEnumerable<string> fallbackPaths)
    {
        foreach (string directory in searchDirectories)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            string candidate = Path.Combine(directory.Trim().Trim('"'), fileName);
            if (fileExists(candidate))
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
}
