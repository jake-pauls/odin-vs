namespace OdinVS.Core;

internal static class FileUriPath
{
    // Uri.LocalPath keeps the %3A in "file:///D%3A/x" and yields "/D:/x", which Windows rejects (NotSupportedException),
    // so decode AbsolutePath ourselves. ols sends drive-letter URIs in this form.
    public static string? ToLocalPath(string? uri)
    {
        if (uri is null || !Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed) || !parsed.IsFile)
        {
            return null;
        }

        string path = Uri.UnescapeDataString(parsed.AbsolutePath);
        if (parsed.Host.Length > 0)
        {
            return @"\\" + parsed.Host + path.Replace('/', '\\');
        }

        if (path.Length >= 3 && path[0] == '/' && path[2] == ':')
        {
            path = path.Substring(1);
        }

        return path.Replace('/', '\\');
    }
}
