using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using OdinVS.Core;
using OdinVS.LanguageServer;
using Task = System.Threading.Tasks.Task;

namespace OdinVS.ProjectSystem;

// Public because VS creates packages by type. Registered by hand in src/OdinVS/odin.pkgdef (the GUIDs below must match; a
// tier-1 test checks it): it loads when the OdinProject UI context becomes active (a solution containing an .odinproj) and
// checks the odin/ols tools on every activation, so a fixed problem or a new one shows up the next time a solution opens.
[Guid(PackageGuidString)]
public sealed class OdinVSPackage : AsyncPackage
{
    public const string PackageGuidString = "2e4a9b0d-6c1f-4b8e-9d3a-7f5c0e1b2a64";
    public const string OdinProjectContextGuidString = "9c3d7e21-4a5b-4f60-8e19-3b6a1d0c5f72";

    private readonly Log m_Log = Log.Default;
    private int m_Checking;
    private IReadOnlyList<string> m_ToolWarnings = [];
    private IVsInfoBarHost? m_Host;
    private IVsInfoBarUIElement? m_Bar;

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        m_Log.Write("OdinVSPackage initializing");
        string extensionDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        UIContext context = UIContext.FromUIContextGuid(new Guid(OdinProjectContextGuidString));
        context.UIContextChanged += (_, e) =>
        {
            if (e.Activated)
            {
                StartCheck(extensionDirectory);
            }
        };

        if (context.IsActive)
        {
            StartCheck(extensionDirectory);
        }

        // Collection-name collisions are found from the solution's projects, whether or not an .odin file was opened yet.
        OdinCollectionRegistry.Instance.ProblemsChanged += () => JoinableTaskFactory.RunAsync(() => RefreshBarAsync(DisposalToken)).Task.Forget();
    }

    private async Task RefreshBarAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ReplaceInfoBarAsync(CombinedText(), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // VS is shutting down; there is nothing to show.
        }
        catch (Exception ex)
        {
            m_Log.Write($"could not update the Odin info bar: {ex}. Problems are in this log.");
        }
    }

    // Tool warnings (missing odin/ols, version mismatch) and collection problems (name collisions) share one bar.
    private string? CombinedText()
    {
        IEnumerable<string> parts = m_ToolWarnings.Concat(OdinCollectionRegistry.Instance.Problems.Select(p => $"{p.Code}: {p.Message}"));
        string text = string.Join(" ", parts);
        return text.Length == 0 ? null : text;
    }

    // ShowToolWarningsAsync catches everything but cancellation, so the task needs no further observation.
    private void StartCheck(string extensionDirectory) =>
        JoinableTaskFactory.RunAsync(() => ShowToolWarningsAsync(extensionDirectory, DisposalToken)).Task.Forget();

    private async Task ShowToolWarningsAsync(string extensionDirectory, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref m_Checking, 1) is 1)
        {
            return;
        }

        try
        {
            IReadOnlyList<string> warnings = await Task.Run(() => CollectWarnings(extensionDirectory), cancellationToken);
            m_Log.Write(warnings.Count == 0 ? "tool check: odin and ols found, versions match" : $"tool check: {warnings.Count} warning(s)");
            m_ToolWarnings = warnings;
            await ReplaceInfoBarAsync(CombinedText(), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // VS is shutting down; there is nothing to show.
        }
        catch (Exception ex)
        {
            m_Log.Write($"could not show the Odin tool warnings: {ex}. Missing-tool problems are in this log; check odin.exe and the bundled ols.exe by hand.");
        }
        finally
        {
            Interlocked.Exchange(ref m_Checking, 0);
        }
    }

    private IReadOnlyList<string> CollectWarnings(string extensionDirectory)
    {
        ResolvedTools tools = new ToolResolver(new ToolLocator(File.Exists)).Resolve(extensionDirectory, Environment.GetEnvironmentVariable("PATH"));
        string? versionText = tools.Odin is null ? null : new OlsProcess(m_Log).ReadOdinVersion(tools.Odin);
        return ToolWarnings.Compute(tools, versionText, BundledOls.Version);
    }

    // Removes the bar from an earlier check, then shows a new one when text is not null.
    private async Task ReplaceInfoBarAsync(string? text, CancellationToken cancellationToken)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        if (m_Host is not null && m_Bar is not null)
        {
            m_Host.RemoveInfoBar(m_Bar);
        }

        m_Bar = null;
        if (text is null)
        {
            return;
        }

        if (await GetServiceAsync(typeof(SVsShell)) is not IVsShell shell
            || await GetServiceAsync(typeof(SVsInfoBarUIFactory)) is not IVsInfoBarUIFactory factory
            || ErrorHandler.Failed(shell.GetProperty((int)__VSSPROPID7.VSSPROPID_MainWindowInfoBarHost, out object hostObject))
            || hostObject is not IVsInfoBarHost host)
        {
            m_Log.Write($"no info bar host available; warning only logged: {text}");
            return;
        }

        InfoBarModel model = new(
            [new InfoBarTextSpan("Odin: " + text + " "), new InfoBarHyperlink("Open log")],
            KnownMonikers.StatusWarning,
            true);
        IVsInfoBarUIElement element = factory.CreateInfoBar(model);
        LogLinkHandler handler = new(Log.DataDirectory, m_Log, () => m_Bar = null);
        element.Advise(handler, out uint cookie);
        handler.Cookie = cookie;
        host.AddInfoBar(element);
        m_Host = host;
        m_Bar = element;
    }

    private sealed class LogLinkHandler(string dataDirectory, Log log, Action onClosed) : IVsInfoBarUIEvents
    {
        public uint Cookie { get; set; }

        public void OnClosed(IVsInfoBarUIElement infoBarUIElement)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            infoBarUIElement.Unadvise(Cookie);
            onClosed();
        }

        public void OnActionItemClicked(IVsInfoBarUIElement infoBarUIElement, IVsInfoBarActionItem actionItem)
        {
            try
            {
                Process.Start(new ProcessStartInfo(Path.Combine(dataDirectory, "odinvs.log")) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                log.Write($"could not open the log: {ex.Message}. Open {dataDirectory}\\odinvs.log by hand.");
            }
        }
    }
}
