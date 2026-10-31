using System.ComponentModel;
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
public sealed class OdinLanguageClient : ILanguageClient, ILanguageClientCustomMessage2
{
    private readonly Log m_Log = Log.Default;
    private readonly OlsProcess m_Launcher;
    private readonly object m_Gate = new();
    private StartedOls? m_Started;

    public OdinLanguageClient()
    {
        m_Launcher = new OlsProcess(m_Log);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => StopProcess();
    }

    public string Name => "Odin Language Server";

    public IEnumerable<string>? ConfigurationSections => null;

    // ols takes its config here (verified by the tier-2 tests); an ols.json is only found in the workspace root.
    public object? InitializationOptions => m_Started is null ? null : JToken.Parse(OlsConfig.Generate(m_Started.Settings));

    public IEnumerable<string>? FilesToWatch => null;

    public bool ShowNotificationOnInitializeFailed => true;

    public object? MiddleLayer { get; } = new OlsMiddleLayer(Log.Default);

    public object? CustomMessageTarget => null;

    public Task AttachForCustomMessageAsync(StreamJsonRpc.JsonRpc rpc) => Task.CompletedTask;

    public event AsyncEventHandler<EventArgs>? StartAsync;

#pragma warning disable CS0067 // required by ILanguageClient; we never request a stop
    public event AsyncEventHandler<EventArgs>? StopAsync;
#pragma warning restore CS0067

    public async Task<Connection?> ActivateAsync(CancellationToken token)
    {
        await Task.Yield();
        StopProcess();

        StartedOls? next = m_Launcher.Start();
        lock (m_Gate)
        {
            m_Started = next;
        }

        if (next is null)
        {
            return null;
        }

        string odinPath = next.OdinPath;
        _ = Task.Run(() => CheckVersions(odinPath), token);
        return new Connection(next.Process.StandardOutput.BaseStream, next.Process.StandardInput.BaseStream);
    }

    public Task OnLoadedAsync()
    {
        m_Log.Write("OnLoadedAsync");
        return StartAsync is null ? Task.CompletedTask : StartAsync.InvokeAsync(this, EventArgs.Empty);
    }

    public Task OnServerInitializedAsync()
    {
        m_Log.Write("OnServerInitializedAsync");
        return Task.CompletedTask;
    }

    public Task<InitializationFailureContext?> OnServerInitializeFailedAsync(ILanguageClientInitializationInfo initializationState)
    {
        m_Log.Write($"OnServerInitializeFailedAsync: {initializationState.StatusMessage}");
        StopProcess();
        return Task.FromResult<InitializationFailureContext?>(new InitializationFailureContext
        {
            FailureMessage = $"Odin language server failed to initialize: {initializationState.StatusMessage}",
        });
    }

    private void CheckVersions(string odinPath)
    {
        try
        {
            string? warning = VersionCompatibility.Check(m_Launcher.ReadOdinVersion(odinPath), BundledOls.Version);
            m_Log.Write(warning is null ? $"bundled ols {BundledOls.Version} matches installed odin" : $"WARNING: {warning}");
        }
        catch (Exception ex)
        {
            m_Log.Write($"version check failed: {ex}. The check is informational; the language server still runs.");
        }
    }

    private void StopProcess()
    {
        Process? running;
        lock (m_Gate)
        {
            running = m_Started?.Process;
            m_Started = null;
        }

        if (running is null)
        {
            return;
        }

        try
        {
            if (!running.HasExited)
            {
                running.Kill();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            m_Log.Write($"could not stop ols: {ex.Message}. If ols.exe is still running, end it in Task Manager.");
        }
        finally
        {
            running.Dispose();
        }
    }
}
