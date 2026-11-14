namespace OdinVS.Core;

internal sealed record OdinBuildOptions(
    string PackageDirectory,
    string OutputPath,
    bool Debug,
    IEnumerable<KeyValuePair<string, string>> Collections,
    IEnumerable<string> LinkInputs,
    string ExtraArguments,
    string OutputType = "Exe",
    string WorkingDirectory = "");

internal static class OdinBuildArguments
{
    // Flags confirmed in `odin build -help`. -debug implies -o:none and emits the PDB, so it is added only for Debug.
    public static string Build(OdinBuildOptions options)
    {
        List<string> arguments = ["build", Quote(options.PackageDirectory.TrimEnd('\\', '/')), Quote("-out:" + options.OutputPath)];
        string? buildMode = BuildMode(options.OutputType);
        if (buildMode is not null)
        {
            arguments.Add("-build-mode:" + buildMode);
        }

        if (options.Debug)
        {
            arguments.Add("-debug");
        }

        arguments.Add("-strict-style");
        foreach (KeyValuePair<string, string> collection in options.Collections)
        {
            arguments.Add(Quote($"-collection:{collection.Key}={collection.Value.TrimEnd('\\', '/')}"));
        }

        // Odin splits -extra-linker-flags on spaces and ignores quotes (seen with dev-2026-10), so these paths cannot be quoted.
        // Each library is linked with /DEFAULTLIB (like a C++ reference, no foreign import needed) and found through its folder as
        // /LIBPATH. A bare path is not used: radlink reports "Invalid flag" for it when other flags are present.
        string[] libraries = options.LinkInputs.Select(path => ToLinkerPath(path, options.WorkingDirectory)).ToArray();
        if (libraries.Length > 0)
        {
            IEnumerable<string> libraryPaths = libraries
                .Select(path => Path.GetDirectoryName(path) is { Length: > 0 } directory ? directory : ".")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(directory => "/LIBPATH:" + directory);
            // The whole value is one command-line argument (quoted because of the spaces between flags); odin then splits it itself.
            arguments.Add(Quote("-extra-linker-flags:" + string.Join(" ", libraryPaths.Concat(libraries.Select(path => "/DEFAULTLIB:" + Path.GetFileName(path))))));
        }

        arguments.Add("-json-errors");
        if (options.ExtraArguments.Trim().Length > 0)
        {
            arguments.Add(options.ExtraArguments.Trim());
        }

        return string.Join(" ", arguments);
    }

    // Exe is odin's default, so it adds no flag. Unknown values are rejected by the SDK target (ODIN0106) before the task runs.
    public static string? BuildMode(string outputType)
    {
        return outputType.ToLowerInvariant() switch
        {
            "dll" => "dll",
            "lib" => "lib",
            _ => null,
        };
    }

    // A library path as it goes to the linker: relative to odin's working directory when the full path has whitespace (a project folder
    // such as "My Projects" is common, a sibling folder with a space is not), otherwise unchanged.
    public static string ToLinkerPath(string path, string workingDirectory)
    {
        if (!HasWhitespace(path) || workingDirectory.Length == 0)
        {
            return path;
        }

        // Segment by segment, not through Uri: a folder named "a#b", "50%" or "x[1]" would be escaped or cut by Uri.
        string[] from = Path.GetFullPath(workingDirectory).TrimEnd('\\', '/').Split('\\');
        string[] to = Path.GetFullPath(path).Split('\\');
        int common = 0;
        while (common < from.Length && common < to.Length - 1 && string.Equals(from[common], to[common], StringComparison.OrdinalIgnoreCase))
        {
            common++;
        }

        // Nothing in common: another drive, or (a UNC path starts with two empty segments, then server and share) another share.
        int minimumShared = path.StartsWith(@"\\", StringComparison.Ordinal) ? 4 : 1;
        if (common < minimumShared)
        {
            return path;
        }

        IEnumerable<string> segments = Enumerable.Repeat("..", from.Length - common).Concat(to.Skip(common));
        return string.Join("\\", segments);
    }

    public static bool HasWhitespace(string value)
    {
        return value.IndexOfAny([' ', '\t']) >= 0;
    }

    // Windows command-line quoting for paths: wrap when there is whitespace. Paths here never end in a backslash
    // (trimmed above) and cannot contain double quotes, so no backslash escaping is needed.
    private static string Quote(string value)
    {
        return HasWhitespace(value) ? "\"" + value + "\"" : value;
    }
}
