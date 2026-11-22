using System.Diagnostics;

namespace OdinVS.Build.Tests;

// Real odin: OdinOutputType (Exe | Dll | Lib) and Link references. LinkApp (exe) references LinkDll (dll) with ReferenceKind="Link";
// LinkLib is a static library project.
[TestClass]
[TestCategory("RequiresToolchain")]
public sealed class LinkTargetTests
{
    private static string Output(string work, string project, string extension) => Path.Combine(work, project, "bin", "Debug", project + extension);

    private static MsBuildResult Build(string work, string project, string targets = "Build", params string[] properties) =>
        MsBuildSandbox.Instance.Build(Path.Combine(work, project), project + ".odinproj", targets, properties);

    private static string Run(string exe)
    {
        using Process run = Process.Start(new ProcessStartInfo(exe) { RedirectStandardOutput = true, UseShellExecute = false })!;
        string stdout = run.StandardOutput.ReadToEnd();
        run.WaitForExit();
        return stdout;
    }

    [TestMethod]
    public void ADllProjectBuildsADllAndItsImportLibraryAndCleanRemovesBoth()
    {
        string work = MsBuildSandbox.Instance.CopySamples("LinkDll");
        MsBuildResult build = Build(work, "LinkDll");
        Assert.AreEqual(0, build.ExitCode, build.Output);
        StringAssert.Contains(build.Output, "-build-mode:dll");
        Assert.IsTrue(File.Exists(Output(work, "LinkDll", ".dll")), build.Output);
        Assert.IsTrue(File.Exists(Output(work, "LinkDll", ".lib")), build.Output);

        MsBuildResult clean = Build(work, "LinkDll", "Clean");
        Assert.AreEqual(0, clean.ExitCode, clean.Output);
        Assert.IsFalse(File.Exists(Output(work, "LinkDll", ".dll")));
        Assert.IsFalse(File.Exists(Output(work, "LinkDll", ".lib")));
    }

    [TestMethod]
    public void AnExecutableLinksAgainstAReferencedDllAndRuns()
    {
        string work = MsBuildSandbox.Instance.CopySamples("LinkApp", "LinkDll");
        MsBuildResult build = Build(work, "LinkApp");
        Assert.AreEqual(0, build.ExitCode, build.Output);
        StringAssert.Contains(build.Output, "/DEFAULTLIB:LinkDll.lib");
        Assert.IsTrue(File.Exists(Path.Combine(work, "LinkApp", "bin", "Debug", "LinkDll.dll")), "the DLL is copied next to the exe");
        StringAssert.Contains(Run(Output(work, "LinkApp", ".exe")), "LinkApp 6 * 7 = 42");
    }

    [TestMethod]
    public void CleanRemovesTheDllsCopiedFromLinkReferences()
    {
        string work = MsBuildSandbox.Instance.CopySamples("LinkApp", "LinkDll");
        Assert.AreEqual(0, Build(work, "LinkApp").ExitCode);
        string copied = Path.Combine(work, "LinkApp", "bin", "Debug", "LinkDll.dll");
        Assert.IsTrue(File.Exists(copied));
        Assert.IsEmpty(Directory.GetFiles(Path.Combine(work, "LinkApp", "bin", "Debug"), "*.txt"), "no bookkeeping file may sit in the output folder");
        Assert.IsTrue(File.Exists(Path.Combine(work, "LinkApp", "obj", "Debug", "LinkApp.FileListAbsolute.txt")), "the list lives in obj");

        MsBuildResult clean = Build(work, "LinkApp", "Clean");
        Assert.AreEqual(0, clean.ExitCode, clean.Output);
        Assert.IsFalse(File.Exists(copied), "the copied DLL must go");
        Assert.IsFalse(File.Exists(Output(work, "LinkApp", ".exe")));
        Assert.IsTrue(File.Exists(Output(work, "LinkDll", ".dll")), "Clean of one project leaves the referenced project's output");
        Assert.IsEmpty(Directory.GetFiles(Path.Combine(work, "LinkApp", "bin", "Debug")), "nothing of ours may be left in the output folder");
    }

    [TestMethod]
    public void TheGraphWalkPassesTheConfigurationToReferencedProjects()
    {
        // The walk asks each referenced project for its node through the build engine; if the Release setting did not reach
        // LinkDll, its library would be looked up under bin\Debug and the link would fail.
        string work = MsBuildSandbox.Instance.CopySamples("LinkApp", "LinkDll");
        MsBuildResult build = Build(work, "LinkApp", "Build", "Configuration=Release");
        Assert.AreEqual(0, build.ExitCode, build.Output);
        StringAssert.Contains(build.Output, @"LinkDll\bin\Release");
        Assert.IsTrue(File.Exists(Path.Combine(work, "LinkApp", "bin", "Release", "LinkApp.exe")), build.Output);
        Assert.IsFalse(Directory.Exists(Path.Combine(work, "LinkDll", "bin", "Debug")), "nothing may be built for Debug");
    }

    [TestMethod]
    public void LinkingWorksWhenTheProjectsLiveInAFolderWithSpaces()
    {
        // odin splits -extra-linker-flags on spaces, so the task passes the library relative to the project folder.
        string work = MsBuildSandbox.Instance.CopySamplesUnder("my work", "LinkApp", "LinkDll");
        MsBuildResult build = Build(work, "LinkApp");
        Assert.AreEqual(0, build.ExitCode, build.Output);
        StringAssert.Contains(build.Output, @"/LIBPATH:..\LinkDll\bin\Debug /DEFAULTLIB:LinkDll.lib");
        StringAssert.Contains(Run(Output(work, "LinkApp", ".exe")), "LinkApp 6 * 7 = 42");
    }

    [TestMethod]
    public void ALibraryPathWithSpacesThatCannotBeRelativeIsReported()
    {
        string work = MsBuildSandbox.Instance.CopySamples("LinkApp", "LinkDll");
        MsBuildResult build = Build(work, "LinkApp", "Build", @"OdinLinkLibrary=C:\no such folder\x.lib");
        Assert.AreNotEqual(0, build.ExitCode, build.Output);
        StringAssert.Contains(build.Output, "ODIN0107");
    }

    [TestMethod]
    public void AChangedDllRebuildsTheExecutable()
    {
        string work = MsBuildSandbox.Instance.CopySamples("LinkApp", "LinkDll");
        Assert.AreEqual(0, Build(work, "LinkApp").ExitCode);
        MsBuildResult unchanged = Build(work, "LinkApp");
        Assert.AreEqual(0, unchanged.ExitCode, unchanged.Output);
        Assert.DoesNotContain("ODIN: ", unchanged.Output);

        File.SetLastWriteTimeUtc(Path.Combine(work, "LinkDll", "src", "dll.odin"), DateTime.UtcNow.AddMinutes(1));
        MsBuildResult changed = Build(work, "LinkApp");
        Assert.AreEqual(0, changed.ExitCode, changed.Output);
        StringAssert.Contains(changed.Output, "ODIN: ");
    }

    [TestMethod]
    public void ADeletedRuntimeDllIsCopiedAgainWithoutRebuildingTheExecutable()
    {
        string work = MsBuildSandbox.Instance.CopySamples("LinkApp", "LinkDll");
        Assert.AreEqual(0, Build(work, "LinkApp").ExitCode);
        string copy = Path.Combine(work, "LinkApp", "bin", "Debug", "LinkDll.dll");
        string copiedPdb = Path.Combine(work, "LinkApp", "bin", "Debug", "LinkDll.pdb");
        Assert.IsTrue(File.Exists(copiedPdb), "the DLL's PDB is copied with it");
        File.Delete(copy);
        File.Delete(copiedPdb);

        MsBuildResult again = Build(work, "LinkApp");
        Assert.AreEqual(0, again.ExitCode, again.Output);
        Assert.DoesNotContain("ODIN: ", again.Output, "the executable is up to date");
        Assert.IsTrue(File.Exists(copy), "the DLL is back next to the exe");
        Assert.IsTrue(File.Exists(copiedPdb));
    }

    [TestMethod]
    public void ALibraryProjectAsksOdinForAStaticLibrary()
    {
        // Pins the wiring only: with Odin dev-2026-10 the static-library link fails on Windows (see docs/milestones.md, M6), so the
        // exit code is not asserted. When odin is fixed, assert the .lib exists here.
        string work = MsBuildSandbox.Instance.CopySamples("LinkLib");
        MsBuildResult build = Build(work, "LinkLib");
        StringAssert.Contains(build.Output, "-build-mode:lib");
        StringAssert.Contains(build.Output, "LinkLib.lib");
    }

    [TestMethod]
    public void AnUnknownOutputTypeIsRejected()
    {
        string work = MsBuildSandbox.Instance.CopySamples("LinkDll");
        MsBuildResult build = Build(work, "LinkDll", "Build", "OdinOutputType=Shared");
        Assert.AreNotEqual(0, build.ExitCode, build.Output);
        StringAssert.Contains(build.Output, "ODIN0106");
    }
}
