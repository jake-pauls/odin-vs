using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace OdinVS.Core.Tests;

// Guards how the Concord debugger component (src/OdinVS.Debugger, milestone M5.5) is wired into the product VSIX. None of this
// shows up in a passing build: a wrong class name or a missing manifest asset only means VS silently never loads the component.
// These are source-level checks; the Exp checklist (milestone G6) is what proves it loads and displays.
[TestClass]
public sealed class DebuggerComponentRegistrationTests
{
    private static readonly XNamespace m_Vsdconfig = "http://schemas.microsoft.com/vstudio/vsdconfig/2008";
    private static readonly XNamespace m_Vsx = "http://schemas.microsoft.com/developer/vsx-schema/2011";

    private static string Src(params string[] parts) => Path.Combine([RepoPaths.Root, "src", .. parts]);

    private static XElement Component() =>
        XDocument.Load(Src("OdinVS.Debugger", "OdinVS.Debugger.vsdconfigxml")).Root!.Element(m_Vsdconfig + "ManagedComponent")!;

    [TestMethod]
    public void ComponentAssemblyNameMatchesTheProject()
    {
        Assert.AreEqual("OdinVS.Debugger", (string?)Component().Attribute("AssemblyName"));
        Assert.IsTrue(File.Exists(Src("OdinVS.Debugger", "OdinVS.Debugger.csproj")));
    }

    [TestMethod]
    public void EveryRegisteredClassExistsInTheComponentSource()
    {
        string sources = string.Join('\n', Directory.GetFiles(Src("OdinVS.Debugger"), "*.cs").Select(File.ReadAllText));
        string[] classes = [.. Component().Elements(m_Vsdconfig + "Class").Select(c => (string)c.Attribute("Name")!)];
        Assert.IsNotEmpty(classes);
        foreach (string full in classes)
        {
            int dot = full.LastIndexOf('.');
            string ns = full[..dot];
            string name = full[(dot + 1)..];
            Assert.IsTrue(sources.Contains($"namespace {ns};", StringComparison.Ordinal), $"namespace {ns} not found for {full}");
            Assert.IsTrue(Regex.IsMatch(sources, $@"public sealed class {name}\b"), $"{full} must be a public class: Concord creates it by name");
        }
    }

    [TestMethod]
    public void EvaluatorIsOnlyRegisteredForLanguageC()
    {
        XElement group = Component().Elements(m_Vsdconfig + "Class")
            .Single(c => (string?)c.Attribute("Name") == "OdinVS.Debugger.OdinEvaluator")
            .Descendants(m_Vsdconfig + "InterfaceGroup").Single();
        Assert.AreEqual("DkmLanguageId.C", (string?)group.Descendants(m_Vsdconfig + "LanguageId").Single().Attribute("RequiredValue"));
        Assert.AreEqual("IDkmLanguageExpressionEvaluator", (string?)group.Element(m_Vsdconfig + "Interface")!.Attribute("Name"));
        Assert.IsNull(group.Element(m_Vsdconfig + "NoFilter"), "an unfiltered registration would run for every debuggee, C# and C++ included");
    }

    [TestMethod]
    public void ThereIsNoNoFilterRegistrationAtAll()
    {
        // The spike's ModuleLoadLogger wrote the module paths of every debuggee to disk; it must not come back.
        Assert.AreEqual(0, Component().Descendants(m_Vsdconfig + "NoFilter").Count());
    }

    [TestMethod]
    public void ManifestShipsTheCompiledConfigAsADebuggerEngineExtension()
    {
        XDocument manifest = XDocument.Load(Src("OdinVS", "source.extension.vsixmanifest"));
        string[] assets = [.. manifest.Descendants(m_Vsx + "Asset").Select(a => $"{(string?)a.Attribute("Type")}|{(string?)a.Attribute("Path")}")];
        CollectionAssert.Contains(assets, "DebuggerEngineExtension|OdinVS.Debugger.vsdconfig");
    }

    [TestMethod]
    public void ProductProjectBuildsAndShipsTheComponent()
    {
        string csproj = File.ReadAllText(Src("OdinVS", "OdinVS.csproj"));
        StringAssert.Contains(csproj, @"..\OdinVS.Debugger\OdinVS.Debugger.csproj");
        StringAssert.Contains(csproj, "OdinVS.Debugger.vsdconfig");
        StringAssert.Contains(File.ReadAllText(Src("OdinVS.Debugger", "OdinVS.Debugger.csproj")), "VsdConfigXmlFiles Include=\"OdinVS.Debugger.vsdconfigxml\"");
    }

    [TestMethod]
    public void TheNatvisIsGoneEverywhere()
    {
        // The `string` natvis entry also changed unrelated C++ types named `string`; the component shows Odin strings itself.
        Assert.IsFalse(File.Exists(Src("OdinVS", "Visualizers", "odin.natvis")));
        Assert.IsFalse(File.ReadAllText(Src("OdinVS", "OdinVS.csproj")).Contains("natvis", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(File.ReadAllText(Src("OdinVS", "source.extension.vsixmanifest")).Contains("natvis", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(File.ReadAllText(Src("OdinVS", "source.extension.vsixmanifest")).Contains("NativeVisualizer", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ComponentStaysScopedToTheOdinGate()
    {
        // The evaluator forwards everything to the native evaluator unless the frame's module is an Odin module.
        string evaluator = File.ReadAllText(Src("OdinVS.Debugger", "OdinEvaluator.cs"));
        StringAssert.Contains(evaluator, "OdinModules.IsOdinFrame(");
        string modules = File.ReadAllText(Src("OdinVS.Debugger", "OdinModules.cs"));
        StringAssert.Contains(modules, "\".odin\"");
    }
}
