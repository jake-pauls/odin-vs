using System.Text;

namespace OdinVS.Core;

internal static class OlsConfig
{
    public static string Generate(OlsSettings settings)
    {
        StringBuilder json = new();
        json.Append("{\n");
        json.Append("  \"odin_command\": ").Append(Quote(settings.OdinCommand)).Append(",\n");
        json.Append("  \"checker_path\": [").Append(string.Join(", ", settings.CheckerPaths.Select(Quote))).Append("],\n");
        json.Append("  \"collections\": [");
        json.Append(string.Join(", ", settings.Collections.Select(c => $"{{ \"name\": {Quote(c.Key)}, \"path\": {Quote(c.Value)} }}")));
        json.Append("]\n}\n");
        return json.ToString();
    }

    private static string Quote(string value)
    {
        StringBuilder quoted = new("\"");
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': quoted.Append("\\\""); break;
                case '\\': quoted.Append("\\\\"); break;
                case '\n': quoted.Append("\\n"); break;
                case '\r': quoted.Append("\\r"); break;
                case '\t': quoted.Append("\\t"); break;
                default:
                    if (c < ' ')
                    {
                        quoted.Append("\\u").Append(((int)c).ToString("x4"));
                    }
                    else
                    {
                        quoted.Append(c);
                    }

                    break;
            }
        }

        return quoted.Append('"').ToString();
    }
}
