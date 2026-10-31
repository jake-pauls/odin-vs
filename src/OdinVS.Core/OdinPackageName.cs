using System.Text.RegularExpressions;

namespace OdinVS.Core;

internal static class OdinPackageName
{
    private static readonly Regex BlockComment = new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex Declaration = new(@"^[ \t]*package[ \t]+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled | RegexOptions.Multiline);

    // The identifier in the file's `package` declaration, or null when there is none (or the file is unreadable).
    public static string? Find(string? sourceText)
    {
        if (string.IsNullOrEmpty(sourceText))
        {
            return null;
        }

        string withoutBlockComments = BlockComment.Replace(sourceText, string.Empty);
        Match match = Declaration.Match(withoutBlockComments);
        return match.Success ? match.Groups[1].Value : null;
    }
}
