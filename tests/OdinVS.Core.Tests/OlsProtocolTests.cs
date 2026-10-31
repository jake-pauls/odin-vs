using System.Text.Json.Nodes;

namespace OdinVS.Core.Tests;

// Tier 2: talks to the pinned ols over stdio. Needs Odin installed at the documented location.
[TestClass]
[TestCategory("RequiresToolchain")]
public sealed class OlsProtocolTests
{
    private const string OdinPath = @"C:\Program Files\Odin\odin\dist\odin.exe";

    private static string RepoRoot { get; } = FindRepoRoot();

    private static string OlsPath => Path.Combine(RepoRoot, "src", "OdinVS", "ols", "ols.exe");

    private static string FixturePath(string kind) =>
        Path.Combine(RepoRoot, "tests", "samples", "LanguageServer", kind, "main.odin");

    [TestInitialize]
    public void RequireToolchain()
    {
        if (!File.Exists(OdinPath) || !File.Exists(OlsPath))
        {
            Assert.Inconclusive("Odin or the bundled ols.exe is not installed.");
        }
    }

    [TestMethod]
    public void BrokenFileGivesDiagnosticsOnSave_ConfigInWorkspaceRoot()
    {
        string workspace = NewWorkspace("broken");
        WriteConfig(workspace, workspace);

        using OlsClient client = OlsClient.Start(OlsPath, Path.GetTempPath());
        JsonNode? published = Run(client, workspace, rootUri: ToUri(workspace), "broken");

        Assert.IsNotNull(published, "no non-empty publishDiagnostics after didSave: " + string.Join(" | ", client.Stderr));
        Assert.IsGreaterThanOrEqualTo(1, published["params"]!["diagnostics"]!.AsArray().Count);
    }

    // Observed 2026-10-30: an ols.json that is only in ols's working directory (no rootUri) is ignored,
    // so squiggles never appear for a lone file. Config goes through initializationOptions instead.
    [TestMethod]
    public void ConfigOnlyInWorkingDirectoryIsIgnored_NoRootUri()
    {
        string workspace = NewWorkspace("broken");
        string configDirectory = Directory.CreateTempSubdirectory("odinvs-cfg").FullName;
        WriteConfig(configDirectory, workspace);

        using OlsClient client = OlsClient.Start(OlsPath, configDirectory);
        JsonNode? published = Run(client, workspace, rootUri: null, "good");

        Assert.IsNull(published);
    }

    [TestMethod]
    public void BrokenFileGivesDiagnosticsOnSave_ConfigViaInitializationOptions_NoRootUri()
    {
        string workspace = NewWorkspace("broken");
        JsonObject options = new()
        {
            ["odin_command"] = OdinPath,
            ["checker_path"] = new JsonArray(workspace),
            ["collections"] = new JsonArray(),
        };

        using OlsClient client = OlsClient.Start(OlsPath, Path.GetTempPath());
        JsonNode? published = Run(client, workspace, rootUri: null, "broken", options);

        Assert.IsNotNull(published, "no non-empty publishDiagnostics after didSave: " + string.Join(" | ", client.Stderr));
    }

    [TestMethod]
    public void DefaultConfigViaInitializationOptions_LooseFileGetsDiagnostics()
    {
        string workspace = NewWorkspace("broken");
        JsonObject options = new()
        {
            ["odin_command"] = OdinPath,
            ["checker_path"] = new JsonArray(),
            ["collections"] = new JsonArray(),
        };

        using OlsClient client = OlsClient.Start(OlsPath, Path.GetTempPath());
        JsonNode? published = Run(client, workspace, rootUri: null, "broken", options);

        Assert.IsNotNull(published, "no diagnostics for a loose file with the default (empty) checker_path");
        Assert.AreEqual(2, published["params"]!["diagnostics"]!.AsArray().Count);
    }

    [TestMethod]
    public void GoodFileGivesNoDiagnostics()
    {
        string workspace = NewWorkspace("good");
        WriteConfig(workspace, workspace);

        using OlsClient client = OlsClient.Start(OlsPath, Path.GetTempPath());
        JsonNode? published = Run(client, workspace, rootUri: ToUri(workspace), "good");

        Assert.IsNull(published);
    }

    [TestMethod]
    public void StartupLogLineYieldsAVersionAndShutdownExitsCleanly()
    {
        string workspace = NewWorkspace("good");
        WriteConfig(workspace, workspace);

        using OlsClient client = OlsClient.Start(OlsPath, workspace);
        client.Request("initialize", InitializeParams(ToUri(workspace)), TimeSpan.FromSeconds(20));
        client.Notify("initialized", new JsonObject());

        Assert.AreEqual(0, client.Shutdown());
        // ols logs nothing on stderr; the version line arrives as a window/logMessage notification.
        string? versionLine = client.Notifications
            .Where(n => n["method"]?.GetValue<string>() == "window/logMessage")
            .Select(n => n["params"]?["message"]?.GetValue<string>())
            .FirstOrDefault(m => m is not null && m.Contains("Starting Odin Language Server"));
        Assert.IsNotNull(versionLine, "no startup logMessage; got: " + string.Join(" | ", client.Notifications.Select(n => n.ToJsonString())));
        Assert.AreEqual(BundledOls.Version, OdinVersion.Parse(versionLine), "bundled ols.exe no longer matches BundledOls.Version");
    }

    [TestMethod]
    public void HoverDefinitionCompletionAndFoldingOnGoodFile()
    {
        string workspace = NewWorkspace("good");
        WriteConfig(workspace, workspace);
        string file = Path.Combine(workspace, "main.odin");
        string uri = ToUri(file);

        using OlsClient client = OlsClient.Start(OlsPath, Path.GetTempPath());
        JsonNode init = client.Request("initialize", InitializeParams(ToUri(workspace)), TimeSpan.FromSeconds(20));
        client.Notify("initialized", new JsonObject());
        client.Notify("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "odin",
                ["version"] = 1,
                ["text"] = File.ReadAllText(file),
            },
        });

        JsonNode hover = client.Request("textDocument/hover", At(uri, 13, 6), TimeSpan.FromSeconds(20));
        JsonNode definition = client.Request("textDocument/definition", At(uri, 13, 6), TimeSpan.FromSeconds(20));
        JsonNode completion = client.Request("textDocument/completion", At(uri, 14, 15), TimeSpan.FromSeconds(20));
        JsonNode folding = client.Request("textDocument/foldingRange", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri } }, TimeSpan.FromSeconds(20));

        // Partial identifiers: where does ols say the replaced span starts?
        JsonNode memberPrefix = client.Request("textDocument/completion", At(uri, 14, 16), TimeSpan.FromSeconds(20));
        JsonNode identPrefix = client.Request("textDocument/completion", At(uri, 13, 9), TimeSpan.FromSeconds(20));
        Console.WriteLine("completion member prefix: " + memberPrefix.ToJsonString());
        Console.WriteLine("completion ident prefix: " + identPrefix.ToJsonString());

        Console.WriteLine("capabilities: " + init["result"]!["capabilities"]!.ToJsonString());
        Console.WriteLine("hover: " + hover.ToJsonString());
        Console.WriteLine("definition: " + definition.ToJsonString());
        Console.WriteLine("completion: " + completion.ToJsonString());
        Console.WriteLine("folding: " + folding.ToJsonString());

        Assert.IsNotNull(hover["result"], "hover returned null");
        Assert.IsNotNull(definition["result"], "definition returned null");
        Assert.IsNotNull(completion["result"], "completion returned null");
    }

    private static JsonObject At(string uri, int line, int character) => new()
    {
        ["textDocument"] = new JsonObject { ["uri"] = uri },
        ["position"] = new JsonObject { ["line"] = line, ["character"] = character },
    };

    private static JsonNode? Run(OlsClient client, string workspace, string? rootUri, string kind, JsonNode? options = null)
    {
        client.Request("initialize", InitializeParams(rootUri, options), TimeSpan.FromSeconds(20));
        client.Notify("initialized", new JsonObject());

        string file = Path.Combine(workspace, "main.odin");
        string uri = ToUri(file);
        client.Notify("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = uri,
                ["languageId"] = "odin",
                ["version"] = 1,
                ["text"] = File.ReadAllText(file),
            },
        });
        client.Notify("textDocument/didSave", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri },
            ["text"] = File.ReadAllText(file),
        });

        return client.WaitForDiagnostics(TimeSpan.FromSeconds(kind == "broken" ? 30 : 8), requireNonEmpty: true);
    }

    private static JsonObject InitializeParams(string? rootUri, JsonNode? options = null) => new()
    {
        ["processId"] = Environment.ProcessId,
        ["rootUri"] = rootUri,
        ["initializationOptions"] = options,
        ["capabilities"] = new JsonObject(),
    };

    private static string NewWorkspace(string kind)
    {
        string workspace = Directory.CreateTempSubdirectory("odinvs-ws").FullName;
        File.Copy(FixturePath(kind), Path.Combine(workspace, "main.odin"));
        return workspace;
    }

    private static void WriteConfig(string directory, string checkerPath) =>
        File.WriteAllText(
            Path.Combine(directory, "ols.json"),
            OlsConfig.Generate(new OlsSettings(OdinPath, [checkerPath], [])));

    private static string ToUri(string path) => new Uri(path).AbsoluteUri;

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CLAUDE.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
