using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using OdinVS.Core;
using OdinVS.LanguageServer;
using Task = System.Threading.Tasks.Task;

namespace OdinVS.ProjectSystem;

// Public because VS creates packages by type. Registered by hand in src/OdinVS/odin.pkgdef (the GUIDs below must match):
// it loads when the OdinProject UI context becomes active (a solution containing an .odinproj) and shows the tool warnings.
[Guid(PackageGuidString)]
public sealed class OdinVSPackage : AsyncPackage
{
    public const string PackageGuidString = "2e4a9b0d-6c1f-4b8e-9d3a-7f5c0e1b2a64";
    public const string OdinProjectContextGuidString = "9c3d7e21-4a5b-4f60-8e19-3b6a1d0c5f72";

    private readonly Log m_Log = Log.Default;
    private bool m_WarningsShown;

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        m_Log.Write("OdinVSPackage initializing");
        string extensionDirectory = ExtensionDirectory();
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        UIContext context = UIContext.FromUIContextGuid(new Guid(OdinProjectContextGuidString));
        context.WhenActivated(() => JoinableTaskFactory.RunAsync(() => ShowToolWarningsAsync(extensionDirectory, DisposalToken)).Task.FileAndForget("odinvs/toolwarnings"));
    }

    private static string ExtensionDirectory() => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;

    private async Task ShowToolWarningsAsync(string extensionDirectory, CancellationToken cancellationToken)
    {
        try
        {
            if (m_WarningsShown)
            {
                return;
            }

            IReadOnlyList<string> warnings = await Task.Run(() => CollectWarnings(extensionDirectory), cancellationToken);
            m_Log.Write(warnings.Count == 0 ? "tool check: odin and ols found, versions match" : $"tool check: {warnings.Count} warning(s)");
            if (warnings.Count == 0)
            {
                return;
            }

            m_WarningsShown = true;
            await ShowInfoBarAsync(string.Join(" ", warnings), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // VS is shutting down; there is nothing to show.
        }
        catch (Exception ex)
        {
            m_Log.Write($"could not show the Odin tool warnings: {ex}. Missing-tool problems are in this log; check odin.exe and the bundled ols.exe by hand.");
        }
    }

    private IReadOnlyList<string> CollectWarnings(string extensionDirectory)
    {
        ResolvedTools tools = new ToolResolver(new ToolLocator(File.Exists)).Resolve(extensionDirectory, Environment.GetEnvironmentVariable("PATH"));
        string? versionText = tools.Odin is null ? null : new OlsProcess(m_Log).ReadOdinVersion(tools.Odin);
        return ToolWarnings.Compute(tools, versionText, BundledOls.Version);
    }

    private async Task ShowInfoBarAsync(string text, CancellationToken cancellationToken)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
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
        LogLinkHandler handler = new(Log.DataDirectory, m_Log);
        element.Advise(handler, out uint cookie);
        handler.Cookie = cookie;
        host.AddInfoBar(element);
    }

    private sealed class LogLinkHandler(string dataDirectory, Log log) : IVsInfoBarUIEvents
    {
        public uint Cookie { get; set; }

        public void OnClosed(IVsInfoBarUIElement infoBarUIElement)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            infoBarUIElement.Unadvise(Cookie);
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
