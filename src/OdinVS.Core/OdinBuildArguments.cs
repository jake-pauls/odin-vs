namespace OdinVS.Core;

internal sealed record OdinBuildOptions(
    string PackageDirectory,
    string OutputPath,
    bool Debug,
    IEnumerable<KeyValuePair<string, string>> Collections,
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
