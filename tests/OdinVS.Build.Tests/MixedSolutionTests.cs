using System.Diagnostics;

namespace OdinVS.Build.Tests;

// Real odin plus Visual Studio's MSBuild: tests/samples/MixedSample.sln holds an Odin app (MixedApp), a C++ DLL (MixedNative.vcxproj)
// and a C# console (MixedCs). The Odin project links the C++ DLL through the Link reference that every .vcxproj reference is.
[TestClass]
[TestCategory("RequiresToolchain")]
public sealed class MixedSolutionTests
{
    [TestMethod]
    public void AnOdinProjectLinksAReferencedCppDllAndTheWholeSolutionBuilds()
    {
        if (!File.Exists(MsBuildSandbox.VisualStudioMsBuild))
        {
            Assert.Inconclusive("Visual Studio's MSBuild is not at " + MsBuildSandbox.VisualStudioMsBuild + " (a .vcxproj cannot be built without it).");
        }

        string work = MsBuildSandbox.Instance.CopySamples("MixedApp", "MixedNative", "MixedCs");
        MsBuildSandbox.Instance.CopySampleFile(work, "MixedSample.sln");

        // The project on its own (this restores Odin.Sdk into the scratch cache); the C++ project is built first by the Link reference.
        MsBuildResult app = MsBuildSandbox.Instance.BuildWithVisualStudio(Path.Combine(work, "MixedApp"), "MixedApp.odinproj", "Build");
        Assert.AreEqual(0, app.ExitCode, app.Output);
        StringAssert.Contains(app.Output, "MixedNative.vcxproj -> ");
        StringAssert.Contains(app.Output, "/DEFAULTLIB:MixedNative.lib");
        string appDirectory = Path.Combine(work, "MixedApp", "bin", "Debug");
        Assert.IsTrue(File.Exists(Path.Combine(appDirectory, "MixedNative.dll")), "the C++ DLL is copied next to the exe");

        using Process run = Process.Start(new ProcessStartInfo(Path.Combine(appDirectory, "MixedApp.exe")) { RedirectStandardOutput = true, UseShellExecute = false })!;
        string stdout = run.StandardOutput.ReadToEnd();
        run.WaitForExit();
        StringAssert.Contains(stdout, "MixedApp 6 * 7 = 42");

        // The whole solution in Release: one configuration switches the Odin, C++ and C# projects together.
        MsBuildResult solution = MsBuildSandbox.Instance.BuildWithVisualStudio(work, "MixedSample.sln", "Build", "Configuration=Release", "Platform=x64");
        Assert.AreEqual(0, solution.ExitCode, solution.Output);
        Assert.IsTrue(File.Exists(Path.Combine(work, "MixedApp", "bin", "Release", "MixedApp.exe")), solution.Output);
        Assert.IsTrue(File.Exists(Path.Combine(work, "MixedNative", "bin", "Release", "MixedNative.dll")), solution.Output);
        Assert.IsTrue(Directory.EnumerateFiles(Path.Combine(work, "MixedCs", "bin"), "MixedCs.dll", SearchOption.AllDirectories).Any(), solution.Output);
    }
}