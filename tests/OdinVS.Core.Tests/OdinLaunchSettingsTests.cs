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
}