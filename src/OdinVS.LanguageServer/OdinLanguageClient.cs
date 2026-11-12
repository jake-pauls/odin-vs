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
    private bool m_Subscribed;

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

    public event AsyncEventHandler<EventArgs>? StopAsync;

    public async Task<Connection?> ActivateAsync(CancellationToken token)
    {
        await Task.Yield();
        StopProcess();

        // Every .odinproj in the solution contributes its collection (ols takes one set per process). A change in the project set
        // restarts ols through StopAsync/StartAsync, which lands here again.
        OdinCollectionRegistry registry = OdinCollectionRegistry.Instance;
        OdinSolutionCollectionsResult collections = registry.Current();
        if (!m_Subscribed)
        {
            m_Subscribed = true;
            registry.CollectionsChanged += () => _ = RestartAsync(CancellationToken.None);
        }

        foreach (OdinReferenceProblem problem in collections.Problems)
        {
            m_Log.Write($"{problem.Code}: {problem.Message}");
        }

        m_Log.Write($"ols collections: {(collections.Collections.Count == 0 ? "(none)" : string.Join(", ", collections.Collections.Select(c => c.Key)))}");
        StartedOls? next = m_Launcher.Start(collections.Collections);
        lock (m_Gate)
        {
            m_Started = next;
        }

        if (next is null)
        {
            return null;
        }

        string odinPath = next.OdinPath;
        string olsPath = next.Process.StartInfo.FileName;
        _ = Task.Run(() => CheckVersions(olsPath, odinPath), token);
        return new Connection(next.Process.StandardOutput.BaseStream, next.Process.StandardInput.BaseStream);
    }

    private async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            m_Log.Write("the solution's Odin collections changed; restarting ols");
            if (StopAsync is not null)
            {
                await StopAsync.InvokeAsync(this, EventArgs.Empty);
            }

            if (StartAsync is not null)
            {
                await StartAsync.InvokeAsync(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            m_Log.Write($"could not restart ols after a collection change: {ex}. Reopen an .odin file or the solution to start it again.");
        }
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

    private void CheckVersions(string olsPath, string odinPath)
    {
        try
        {
            IReadOnlyList<string> warnings = ToolWarnings.Compute(new ResolvedTools(olsPath, odinPath, []), m_Launcher.ReadOdinVersion(odinPath), BundledOls.Version);
            m_Log.Write(warnings.Count == 0 ? $"bundled ols {BundledOls.Version} matches installed odin" : $"WARNING: {string.Join(" ", warnings)}");
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
