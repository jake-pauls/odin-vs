using Microsoft.VisualStudio.LanguageServer.Client;
using Newtonsoft.Json.Linq;
using OdinVS.Core;
using Task = System.Threading.Tasks.Task;

namespace OdinVS.LanguageServer;

// Reshapes ols diagnostics on their way into VS: code becomes ols.<code>, source becomes "ols", and the Error List
// Project column shows the Odin package declared in the file.
internal sealed class OlsMiddleLayer(Log log) : ILanguageClientMiddleLayer2<JToken>
{
    private const string PublishDiagnostics = "textDocument/publishDiagnostics";

    public bool CanHandle(string methodName) => methodName == PublishDiagnostics;

    public Task HandleNotificationAsync(string methodName, JToken methodParam, Func<JToken, Task> sendNotification)
    {
        if (methodParam["diagnostics"] is JArray diagnostics)
        {
            string? package = ReadPackageName(methodParam["uri"]?.ToString());
            foreach (JToken diagnostic in diagnostics)
            {
                if (diagnostic is JObject entry)
                {
                    entry["code"] = OlsDiagnosticCode.Rewrite(entry["code"]?.ToString());
                    entry["source"] = OlsDiagnosticCode.Prefix;
                    if (package is not null)
                    {
                        entry["_vs_projects"] = new JArray(new JObject { ["_vs_projectName"] = package });
                    }
                }
            }
        }

        return sendNotification(methodParam);
    }

    public Task<JToken?> HandleRequestAsync(string methodName, JToken methodParam, Func<JToken, Task<JToken?>> sendRequest) =>
        sendRequest(methodParam);

    // Diagnostics arrive on save, so the file on disk is what ols checked.
    private string? ReadPackageName(string? uri)
    {
        if (uri is null || !Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed) || !parsed.IsFile)
        {
            return null;
        }

        try
        {
            return OdinPackageName.Find(File.ReadAllText(parsed.LocalPath));
        }
        catch (IOException ex)
        {
            log.Write($"could not read {parsed.LocalPath} for its package name: {ex.Message}");
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            log.Write($"could not read {parsed.LocalPath} for its package name: {ex.Message}");
            return null;
        }
    }
}
