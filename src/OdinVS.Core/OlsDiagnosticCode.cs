namespace OdinVS.Core;

internal static class OlsDiagnosticCode
{
    public const string Prefix = "ols";

    // ols sends codes such as "checker" and "syntax_error"; surface them as "ols.checker" etc. so the origin is clear.
    public static string Rewrite(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Prefix;
        }

        string trimmed = code!.Trim();
        return trimmed == Prefix || trimmed.StartsWith(Prefix + ".", StringComparison.Ordinal)
            ? trimmed
            : $"{Prefix}.{trimmed}";
    }
}
