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
