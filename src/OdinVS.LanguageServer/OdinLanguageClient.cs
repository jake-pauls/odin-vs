using System.ComponentModel.Composition;
using System.Diagnostics;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;
using Newtonsoft.Json.Linq;
using OdinVS.Core;
using Task = System.Threading.Tasks.Task;

namespace OdinVS.LanguageServer;

// Public and MEF-exported because VS discovers language clients through composition.
[ContentType(OdinContentType.Name)]
[Export(typeof(ILanguageClient))]
public sealed class OdinLanguageClient : ILanguageClient
{
    private readonly Log log = Log.Default;
    private readonly OlsProcess launcher;
    private StartedOls? started;

    public OdinLanguageClient() => launcher = new OlsProcess(log);

    public string Name => "Odin Language Server";

    public IEnumerable<string>? ConfigurationSections => null;

    // ols takes its config here (verified by the tier-2 tests); an ols.json is only found in the workspace root.
    public object? InitializationOptions => started is null ? null : JToken.Parse(OlsConfig.Generate(started.Settings));

    public IEnumerable<string>? FilesToWatch => null;

    public bool ShowNotificationOnInitializeFailed => true;

    public event AsyncEventHandler<EventArgs>? StartAsync;

#pragma warning disable CS0067 // required by ILanguageClient; we never request a stop
    public event AsyncEventHandler<EventArgs>? StopAsync;
#pragma warning restore CS0067

    public async Task<Connection?> ActivateAsync(CancellationToken token)
    {
        await Task.Yield();
        StopProcess();

        started = launcher.Start();
        if (started is null)
        {
            return null;
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => StopProcess();
        string odinPath = started.OdinPath;
        _ = Task.Run(() => CheckVersions(odinPath), token);
        return new Connection(started.Process.StandardOutput.BaseStream, started.Process.StandardInput.BaseStream);
    }

    public Task OnLoadedAsync()
    {
        log.Write("OnLoadedAsync");
        return StartAsync is null ? Task.CompletedTask : StartAsync.InvokeAsync(this, EventArgs.Empty);
    }

    public Task OnServerInitializedAsync()
    {
        log.Write("OnServerInitializedAsync");
        return Task.CompletedTask;
    }

    public Task<InitializationFailureContext?> OnServerInitializeFailedAsync(ILanguageClientInitializationInfo initializationState)
    {
        log.Write($"OnServerInitializeFailedAsync: {initializationState.StatusMessage}");
        StopProcess();
        return Task.FromResult<InitializationFailureContext?>(new InitializationFailureContext
        {
            FailureMessage = $"Odin language server failed to initialize: {initializationState.StatusMessage}",
        });
    }

    private void CheckVersions(string odinPath)
    {
        string? warning = VersionCompatibility.Check(launcher.ReadOdinVersion(odinPath), BundledOls.Version);
        log.Write(warning is null ? $"bundled ols {BundledOls.Version} matches installed odin" : $"WARNING: {warning}");
    }

    private void StopProcess()
    {
        Process? running = started?.Process;
        started = null;
        if (running is null)
        {
            return;
        }

        try
        {
            if (!running.HasExited && !running.WaitForExit(2000))
            {
                running.Kill();
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            running.Dispose();
        }
    }
}
