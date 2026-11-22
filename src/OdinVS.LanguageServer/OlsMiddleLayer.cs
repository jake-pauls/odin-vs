using Microsoft.VisualStudio.LanguageServer.Client;
using Newtonsoft.Json.Linq;
using OdinVS.Core;
using Task = System.Threading.Tasks.Task;

namespace OdinVS.LanguageServer;

// Reshapes ols diagnostics on their way into VS: code becomes ols.<code>. Anything that goes wrong here must still
// forward the diagnostics as received, or VS shows none (see docs/dev/pitfalls.md).
internal sealed class OlsMiddleLayer(Log log) : ILanguageClientMiddleLayer2<JToken>
{
    private const string PublishDiagnostics = "textDocument/publishDiagnostics";

    public bool CanHandle(string methodName) => methodName is PublishDiagnostics;

    public Task HandleNotificationAsync(string methodName, JToken methodParam, Func<JToken, Task> sendNotification)
    {
        try
        {
            if (methodParam["diagnostics"] is JArray diagnostics)
            {
                log.Write($"publishDiagnostics {methodParam["uri"]}: {diagnostics.Count} diagnostics");
                foreach (JToken diagnostic in diagnostics)
                {
                    if (diagnostic is JObject entry)
                    {
                        entry["code"] = OlsDiagnosticCode.Rewrite(entry["code"]?.ToString());
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log.Write($"could not reshape diagnostics, forwarding as received: {ex}");
        }

        return sendNotification(methodParam);
    }

    public Task<JToken?> HandleRequestAsync(string methodName, JToken methodParam, Func<JToken, Task<JToken?>> sendRequest) =>
        sendRequest(methodParam);
}
