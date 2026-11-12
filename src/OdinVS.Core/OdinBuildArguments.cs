namespace OdinVS.Core;

internal sealed record OdinBuildOptions(
    string PackageDirectory,
    string OutputPath,
    bool Debug,
    IEnumerable<KeyValuePair<string, string>> Collections,
    IEnumerable<string> LinkInputs,
    string ExtraArguments);

internal static class OdinBuildArguments
{
    // Flags confirmed in `odin build -help`. -debug implies -o:none and emits the PDB, so it is added only for Debug.
    public static string Build(OdinBuildOptions options)
    {
        List<string> arguments = ["build", Quote(options.PackageDirectory.TrimEnd('\\', '/')), Quote("-out:" + options.OutputPath)];
        if (options.Debug)
        {
            arguments.Add("-debug");
        }

        arguments.Add("-strict-style");
        foreach (KeyValuePair<string, string> collection in options.Collections)
        {
            arguments.Add(Quote($"-collection:{collection.Key}={collection.Value.TrimEnd('\\', '/')}"));
        }

        // Library files of Link references go to the linker as a whole. A path with spaces is quoted inside the flag value.
        string[] linkInputs = options.LinkInputs.Select(path => path.IndexOfAny([' ', '\t']) < 0 ? path : "\"" + path + "\"").ToArray();
        if (linkInputs.Length > 0)
        {
            arguments.Add("\"-extra-linker-flags:" + string.Join(" ", linkInputs).Replace("\"", "\\\"") + "\"");
        }

        arguments.Add("-json-errors");
        if (options.ExtraArguments.Trim().Length > 0)
        {
            arguments.Add(options.ExtraArguments.Trim());
        }

        return string.Join(" ", arguments);
    }

    // Windows command-line quoting for paths: wrap when there is whitespace. Paths here never end in a backslash
    // (trimmed above) and cannot contain double quotes, so no backslash escaping is needed.
    private static string Quote(string value)
    {
        return value.IndexOfAny([' ', '\t']) < 0 ? value : "\"" + value + "\"";
    }
}
