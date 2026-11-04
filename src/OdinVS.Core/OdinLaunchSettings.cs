namespace OdinVS.Core;

// What F5 / Ctrl+F5 launch for an .odinproj, resolved from the evaluated properties (TargetPath, LocalDebuggerWorkingDirectory,
// LocalDebuggerCommandArguments).
public sealed record OdinLaunchSettings(string Executable, string Arguments, string WorkingDirectory)
{
    public static OdinLaunchSettings Resolve(string? targetPath, string? workingDirectory, string? arguments, string projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new InvalidOperationException("TargetPath is empty, so there is no executable to launch. Check the .odinproj for a TargetPath or TargetName override.");
        }

        string directory = string.IsNullOrWhiteSpace(workingDirectory)
            ? projectDirectory
            : Path.GetFullPath(Path.Combine(projectDirectory, workingDirectory));
        return new OdinLaunchSettings(Path.GetFullPath(Path.Combine(projectDirectory, targetPath)), arguments ?? string.Empty, directory);
    }
}