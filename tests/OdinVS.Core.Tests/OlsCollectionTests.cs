using System.Diagnostics;
using System.Text.Json.Nodes;

namespace OdinVS.Core.Tests;

// Tier 2: how the pinned ols takes collections (found 2026-11-11 while planning editor support across projects). Two projects use
// the same collection name `lib` for different folders; hover on `pkg.value` / `pkg.other` shows which collection ols used.
// Facts these tests pin, and the design that follows: ols has ONE collection set per process, read at initialize; a second workspace
// folder, an ols.json next to a project, and workspace/didChangeConfiguration do not change it. Per-project accuracy therefore needs
// one ols per distinct collection set. If a bundled-ols bump changes any of this, the language client can do better.
[TestClass]
[TestCategory("RequiresToolchain")]
public sealed class OlsCollectionTests
{
    private const string OdinPath = ToolResolver.OdinFallback;

    private static string OlsPath => Path.Combine(RepoPaths.BundledOlsDirectory, "ols.exe");

    private sealed record Layout(string CollectionA, string CollectionB, string ProjectA, string ProjectB, string FileA, string FileB);

    [TestInitialize]
    public void RequireToolchain()
    {
        if (!File.Exists(OdinPath) || !File.Exists(OlsPath))
        {
            Assert.Inconclusive("Odin or the bundled ols.exe is not installed.");
        }
    }

    private static Layout Create()
    {
        string root = Directory.CreateTempSubdirectory("odinvs-col").FullName;
        string collectionA = Path.Combine(root, "colA");
        string collectionB = Path.Combine(root, "colB");
        Directory.CreateDirectory(Path.Combine(collectionA, "pkg"));
        Directory.CreateDirectory(Path.Combine(collectionB, "pkg"));
        File.WriteAllText(Path.Combine(collectionA, "pkg", "pkg.odin"), "package pkg\n\nvalue :: proc() -> int {\n\treturn 1\n}\n");
        File.WriteAllText(Path.Combine(collectionB, "pkg", "pkg.odin"), "package pkg\n\nother :: proc() -> int {\n\treturn 2\n}\n");
        string projectA = Path.Combine(root, "projA", "src");
        string projectB = Path.Combine(root, "projB", "src");
        Directory.CreateDirectory(projectA);
        Directory.CreateDirectory(projectB);
        string fileA = Path.Combine(projectA, "main.odin");
        string fileB = Path.Combine(projectB, "main.odin");
        File.WriteAllText(fileA, "package main\n\nimport \"lib:pkg\"\n\nmain :: proc() {\n\tx := pkg.value()\n\t_ = x\n}\n");
        File.WriteAllText(fileB, "package main\n\nimport \"lib:pkg\"\n\nmain :: proc() {\n\tx := pkg.other()\n\t_ = x\n}\n");
        return new Layout(collectionA, collectionB, projectA, projectB, fileA, fileB);
    }

    private static string ToUri(string path) => new Uri(path).AbsoluteUri;

    private static string HoverText(OlsClient client, string file)
    {
        string uri = ToUri(file);
        client.Notify("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri, ["languageId"] = "odin", ["version"] = 1, ["text"] = File.ReadAllText(file) },
        });
        JsonNode hover = client.Request("textDocument/hover", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri },
            ["position"] = new JsonObject { ["line"] = 5, ["character"] = 11 },
        }, TimeSpan.FromSeconds(20));
        return hover["result"]?["contents"]?["value"]?.GetValue<string>() ?? string.Empty;
    }

    private static JsonObject Options(string checker, params string[] collectionPaths) => new()
    {
        ["odin_command"] = OdinPath,
        ["checker_path"] = new JsonArray(checker),
        ["collections"] = new JsonArray(collectionPaths.Select(path => (JsonNode)new JsonObject { ["name"] = "lib", ["path"] = path }).ToArray()),
    };

    private static OlsClient Start(JsonNode? options, string? rootUri = null, JsonNode? folders = null)
    {
        OlsClient client = OlsClient.Start(OlsPath, Path.GetTempPath());
        client.Request("initialize", new JsonObject
        {
            ["processId"] = Environment.ProcessId,
            ["rootUri"] = rootUri,
            ["workspaceFolders"] = folders,
            ["initializationOptions"] = options,
            ["capabilities"] = new JsonObject(),
        }, TimeSpan.FromSeconds(20));
        client.Notify("initialized", new JsonObject());
        return client;
    }

    [TestMethod]
    public void CollectionsFromInitializationOptionsResolveAnImportAcrossProjects()
    {
        Layout layout = Create();
        using OlsClient client = Start(Options(layout.ProjectA, layout.CollectionA));
        StringAssert.Contains(HoverText(client, layout.FileA), "pkg.value");
    }

    // M7 scale check: the language client gives ols one collection per project in the solution, so a 100-project solution means 100
    // collections at initialize (and a restart when the set changes). Timings are written to the test output.
    [TestMethod]
    public void OneHundredCollectionsInitializeAndResolveAnImportQuickly()
    {
        const int Count = 100;
        string root = Directory.CreateTempSubdirectory("odinvs-col100").FullName;
        JsonArray collections = [];
        for (int i = 1; i <= Count; i++)
        {
            string collection = Path.Combine(root, $"col{i:D3}");
            Directory.CreateDirectory(Path.Combine(collection, "pkg"));
            File.WriteAllText(Path.Combine(collection, "pkg", "pkg.odin"), $"package pkg\n\nvalue{i} :: proc() -> int {{\n\treturn {i}\n}}\n");
            collections.Add(new JsonObject { ["name"] = $"c{i:D3}", ["path"] = collection });
        }

        string project = Path.Combine(root, "app", "src");
        Directory.CreateDirectory(project);
        string file = Path.Combine(project, "main.odin");
        File.WriteAllText(file, "package main\n\nimport \"c050:pkg\"\n\nmain :: proc() {\n\tx := pkg.value50()\n\t_ = x\n}\n");
        JsonObject options = new() { ["odin_command"] = OdinPath, ["checker_path"] = new JsonArray(project), ["collections"] = collections };

        Stopwatch timer = Stopwatch.StartNew();
        using OlsClient client = Start(options);
        TimeSpan initialize = timer.Elapsed;
        timer.Restart();
        string hover = HoverText(client, file);
        TimeSpan firstHover = timer.Elapsed;
        Console.WriteLine($"100 collections: start+initialize {initialize.TotalMilliseconds:F0} ms, first hover {firstHover.TotalMilliseconds:F0} ms");
        StringAssert.Contains(hover, "pkg.value50");
        Assert.IsLessThan(TimeSpan.FromSeconds(10), initialize, "initialize with 100 collections should stay well under 10 s");
    }

    [TestMethod]
    public void OneOlsHasOneCollectionSetSoAProjectWithAnotherRootForTheSameNameGetsNothing()
    {
        Layout layout = Create();
        using OlsClient client = Start(Options(layout.ProjectA, layout.CollectionA));
        StringAssert.Contains(HoverText(client, layout.FileA), "pkg.value");
        Assert.AreEqual(string.Empty, HoverText(client, layout.FileB));
    }

    [TestMethod]
    public void ADuplicateCollectionNameTakesTheLastEntry()
    {
        Layout layout = Create();
        using OlsClient client = Start(Options(layout.ProjectA, layout.CollectionA, layout.CollectionB));
        Assert.AreEqual(string.Empty, HoverText(client, layout.FileA));
        StringAssert.Contains(HoverText(client, layout.FileB), "pkg.other");
    }

    [TestMethod]
    public void ASecondWorkspaceFolderWithItsOwnOlsJsonIsNotUsed()
    {
        Layout layout = Create();
        string rootA = Path.GetDirectoryName(layout.ProjectA)!;
        string rootB = Path.GetDirectoryName(layout.ProjectB)!;
        File.WriteAllText(Path.Combine(rootA, "ols.json"), OlsConfig.Generate(new OlsSettings(OdinPath, [layout.ProjectA], [new KeyValuePair<string, string>("lib", layout.CollectionA)])));
        File.WriteAllText(Path.Combine(rootB, "ols.json"), OlsConfig.Generate(new OlsSettings(OdinPath, [layout.ProjectB], [new KeyValuePair<string, string>("lib", layout.CollectionB)])));
        JsonArray folders = new(new JsonObject { ["uri"] = ToUri(rootA), ["name"] = "A" }, new JsonObject { ["uri"] = ToUri(rootB), ["name"] = "B" });
        using OlsClient client = Start(null, ToUri(rootA), folders);
        StringAssert.Contains(HoverText(client, layout.FileA), "pkg.value");
        Assert.AreEqual(string.Empty, HoverText(client, layout.FileB));
    }

    [TestMethod]
    public void AnOlsJsonNextToTheProjectIsIgnoredWithoutAWorkspaceRoot()
    {
        Layout layout = Create();
        string rootA = Path.GetDirectoryName(layout.ProjectA)!;
        File.WriteAllText(Path.Combine(rootA, "ols.json"), OlsConfig.Generate(new OlsSettings(OdinPath, [layout.ProjectA], [new KeyValuePair<string, string>("lib", layout.CollectionA)])));
        using OlsClient client = Start(null);
        Assert.AreEqual(string.Empty, HoverText(client, layout.FileA));
    }

    [TestMethod]
    public void DidChangeConfigurationDoesNotReplaceTheCollections()
    {
        Layout layout = Create();
        using OlsClient client = Start(Options(layout.ProjectA, layout.CollectionA));
        client.Notify("workspace/didChangeConfiguration", new JsonObject { ["settings"] = Options(layout.ProjectB, layout.CollectionB) });
        client.Notify("workspace/didChangeConfiguration", new JsonObject { ["settings"] = new JsonObject { ["ols"] = Options(layout.ProjectB, layout.CollectionB) } });
        Thread.Sleep(500);
        StringAssert.Contains(HoverText(client, layout.FileA), "pkg.value");
        Assert.AreEqual(string.Empty, HoverText(client, layout.FileB));
    }
}
