using System.ComponentModel.Composition;
using System.Diagnostics;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;
using Task = System.Threading.Tasks.Task;

namespace OdinVS.LanguageServer;

// Public and MEF-exported because VS discovers language clients through composition.
[ContentType(OdinContentType.Name)]
[Export(typeof(ILanguageClient))]
public sealed class OdinLanguageClient : ILanguageClient
{
    private readonly Log log = Log.Default;
    private Process? process;

    public string Name => "Odin Language Server";

    public IEnumerable<string>? ConfigurationSections => null;

    public object? InitializationOptions => null;

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

        process = new OlsProcess(log).Start();
        if (process is null)
        {
            return null;
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => StopProcess();
        return new Connection(process.StandardOutput.BaseStream, process.StandardInput.BaseStream);
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

    private void StopProcess()
    {
        Process? running = process;
        process = null;
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
