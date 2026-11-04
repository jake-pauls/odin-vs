using OdinVS.Core;

namespace OdinVS.Core.Tests;

[TestClass]
public sealed class OdinLaunchSettingsTests
{
    private const string ProjectDirectory = @"C:\work\App";

    [TestMethod]
    public void DefaultsToProjectDirectoryAndNoArguments()
    {
        OdinLaunchSettings settings = OdinLaunchSettings.Resolve(@"C:\work\App\bin\Debug\App.exe", null, null, ProjectDirectory);

        Assert.AreEqual(@"C:\work\App\bin\Debug\App.exe", settings.Executable);
        Assert.AreEqual(ProjectDirectory, settings.WorkingDirectory);
        Assert.AreEqual(string.Empty, settings.Arguments);
    }

    [TestMethod]
    public void RelativePathsResolveAgainstTheProjectDirectory()
    {
        OdinLaunchSettings settings = OdinLaunchSettings.Resolve(@"bin\Debug\App.exe", @"run", "--fast x", ProjectDirectory);

        Assert.AreEqual(@"C:\work\App\bin\Debug\App.exe", settings.Executable);
        Assert.AreEqual(@"C:\work\App\run", settings.WorkingDirectory);
        Assert.AreEqual("--fast x", settings.Arguments);
    }

    [TestMethod]
    public void EmptyTargetPathThrowsWithAHint()
    {
        InvalidOperationException ex = Assert.ThrowsExactly<InvalidOperationException>(() => OdinLaunchSettings.Resolve(" ", null, null, ProjectDirectory));

        StringAssert.Contains(ex.Message, "TargetPath");
    }
    [TestMethod]
    public void AbsolutePathsAreKept()
    {
        OdinLaunchSettings settings = OdinLaunchSettings.Resolve(@"D:\out\App.exe", @"E:\run", null, ProjectDirectory);

        Assert.AreEqual(@"D:\out\App.exe", settings.Executable);
        Assert.AreEqual(@"E:\run", settings.WorkingDirectory);
    }

    [TestMethod]
    public void WhitespaceWorkingDirectoryFallsBackToTheProjectDirectory()
    {
        OdinLaunchSettings settings = OdinLaunchSettings.Resolve(@"bin\App.exe", "  ", null, ProjectDirectory);

        Assert.AreEqual(ProjectDirectory, settings.WorkingDirectory);
    }

    [TestMethod]
    public void ParentSegmentsAreNormalized()
    {
        OdinLaunchSettings settings = OdinLaunchSettings.Resolve(@"..\out\App.exe", @"..\shared", null, ProjectDirectory);

        Assert.AreEqual(@"C:\work\out\App.exe", settings.Executable);
        Assert.AreEqual(@"C:\work\shared", settings.WorkingDirectory);
    }
}