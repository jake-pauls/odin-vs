using Microsoft.VisualStudio.LanguageServer.Client;
using Newtonsoft.Json.Linq;
using OdinVS.Core;
using Task = System.Threading.Tasks.Task;

namespace OdinVS.LanguageServer;

// Rewrites ols diagnostics on their way into VS: the code (shown in the Error List and squiggle tooltips) becomes ols.<code>.
internal sealed class OlsMiddleLayer : ILanguageClientMiddleLayer2<JToken>
{
    private const string PublishDiagnostics = "textDocument/publishDiagnostics";

    public bool CanHandle(string methodName) => methodName == PublishDiagnostics;

    public Task HandleNotificationAsync(string methodName, JToken methodParam, Func<JToken, Task> sendNotification)
    {
        if (methodParam["diagnostics"] is JArray diagnostics)
        {
            foreach (JToken diagnostic in diagnostics)
            {
                if (diagnostic is JObject entry)
                {
                    entry["code"] = OlsDiagnosticCode.Rewrite(entry["code"]?.ToString());
                }
            }
        }

        return sendNotification(methodParam);
    }

    public Task<JToken?> HandleRequestAsync(string methodName, JToken methodParam, Func<JToken, Task<JToken?>> sendRequest) =>
        sendRequest(methodParam);
}
