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

    // Same checks, in the same order and wording style, as the C# project system's launch provider (ValidateSettings): a missing
    // executable, then a missing working directory. Returns the message to show, or null when the launch can go ahead.
    public string? Validate(Func<string, bool> fileExists, Func<string, bool> directoryExists, string projectName)
    {
        if (!fileExists(Executable))
        {
            return $"The debug executable '{Executable}' of project '{projectName}' does not exist. Build the project, or check TargetPath in the .odinproj.";
        }

        if (!directoryExists(WorkingDirectory))
        {
            return $"The working directory '{WorkingDirectory}' of project '{projectName}' does not exist. Check LocalDebuggerWorkingDirectory in the .odinproj.";
        }

        return null;
    }
}