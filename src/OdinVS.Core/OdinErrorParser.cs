namespace OdinVS.Core;

internal sealed record OdinDiagnostic(bool IsWarning, string File, int Line, int Column, int EndColumn, string Message);

// Result of reading odin's stderr: the structured diagnostics from -json-errors plus any text that was not part of the JSON
// (linker output, crashes), which the caller reports instead of dropping.
internal sealed record OdinErrorOutput(IReadOnlyList<OdinDiagnostic> Diagnostics, string OtherText);

internal static class OdinErrorParser
{
    // Shape verified against odin dev-2026-10: one JSON object on stderr, {error_count, errors[{type, pos{file,line,column,end_column}, msgs[]}]}.
    // Warnings were never observed with a distinct type; anything but "warning" is treated as an error.
    public static OdinErrorOutput Parse(string stderr)
    {
        string trimmed = stderr.Trim();
        int start = FindJsonStart(trimmed);
        if (start < 0 || !MiniJson.TryParse(trimmed.Substring(start), out object? root) || root is not Dictionary<string, object?> document)
        {
            return new OdinErrorOutput([], trimmed);
        }

        List<OdinDiagnostic> diagnostics = [];
        if (document.TryGetValue("errors", out object? errors) && errors is List<object?> list)
        {
            foreach (object? entry in list)
            {
                if (entry is Dictionary<string, object?> error)
                {
                    diagnostics.Add(ToDiagnostic(error));
                }
            }
        }

        return new OdinErrorOutput(diagnostics, start > 0 ? trimmed.Substring(0, start).Trim() : string.Empty);
    }

    // The JSON object starts at the first line that is just "{"; text before it is other tool output.
    private static int FindJsonStart(string text)
    {
        if (text.StartsWith("{", StringComparison.Ordinal))
        {
            return 0;
        }

        int index = text.IndexOf("\n{", StringComparison.Ordinal);
        return index < 0 ? -1 : index + 1;
    }

    private static OdinDiagnostic ToDiagnostic(Dictionary<string, object?> error)
    {
        Dictionary<string, object?>? pos = error.TryGetValue("pos", out object? p) ? p as Dictionary<string, object?> : null;
        string file = pos is not null && pos.TryGetValue("file", out object? f) && f is string path ? path.Replace('/', '\\') : string.Empty;
        int line = ReadInt(pos, "line");
        // Odin reports column 0 at end of file; MSBuild columns are 1-based.
        int column = Math.Max(ReadInt(pos, "column"), 1);
        int endColumn = ReadInt(pos, "end_column");
        string message = error.TryGetValue("msgs", out object? m) && m is List<object?> msgs
            ? string.Join(" ", msgs.OfType<string>())
            : string.Empty;
        bool isWarning = error.TryGetValue("type", out object? type) && type is "warning";
        return new OdinDiagnostic(isWarning, file, line, column, endColumn, message);
    }

    private static int ReadInt(Dictionary<string, object?>? pos, string key)
    {
        return pos is not null && pos.TryGetValue(key, out object? value) && value is double number ? (int)number : 0;
    }
}
