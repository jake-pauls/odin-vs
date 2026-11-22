# Projects in Visual Studio

- **Open or add a project:** open a `.sln`/`.slnx` that lists `.odinproj` files, or use *File > Open > Project/Solution* on an `.odinproj`; *Add > Existing Project* also works. The project node lists the files its `OdinCompile` items name (a glob such as `src\**\*.odin` tracks files added or removed on disk).
- **Create one:** *File > New > Project > Odin Executable* or *Odin Dynamic Library (DLL)* (or *Add > New Project* in a solution) generates `<Name>.odinproj` and `src\main.odin` (`src\lib.odin` for the DLL template). The project names `Odin.Sdk` with the version this build of the extension packs, so it matches the package you built in [Getting started](../getting-started.md#3-build-the-local-odinsdk-package); it only mismatches if you later repack a different version, in which case change the `Sdk` attribute to match the `nupkg` file name. *Add > New Item > Odin File* adds a source file.
- **Build:** *Build > Build/Rebuild/Clean Solution* (or the project entries) run the same `odin build` command as the command line; errors and warnings go to the Error List and double-click opens the line. The toolbar and *Build > Configuration Manager* switch Debug/Release x64 for every project in the solution; output goes to `bin\<Configuration>`.
- **If another extension that registers `.odinproj` is installed** (for example an early development build of this one), uninstall it; two extensions registering the same project type conflict.
- **Warnings about the toolchain:** when a solution holds an `.odinproj`, an info bar at the top of the window appears if `odin.exe` is not found, the bundled `ols.exe` is missing, or the installed odin is not the version the bundled ols was built for (messages say what to fix; *Open log* opens `%LOCALAPPDATA%\OdinVS\odinvs.log`). The check runs each time a solution containing an `.odinproj` is opened or reloaded, and a new result replaces the previous bar.

Opening a solution also makes Visual Studio evaluate each project's MSBuild file, as it does for C# and C++ projects, so open projects from sources you trust.

## A minimal `.odinproj`

A project uses the `Odin.Sdk` MSBuild SDK and lists its own source files:

```xml
<Project Sdk="Odin.Sdk/<version>">
  <ItemGroup>
    <OdinCompile Include="src\**\*.odin" />
  </ItemGroup>
</Project>
```

Replace `<version>` with the package version you built (for example `0.5.1-local`); see [Project properties](../reference/project-properties.md) for everything else a project can set.

## Building from the command line

From a Developer PowerShell (odin's link step needs the MSVC environment):

```
msbuild MyProject.odinproj /restore /t:Build
msbuild MyProject.odinproj /restore /t:Rebuild /p:Configuration=Release
```

Next: [References](references.md), [Debugging](debugging.md), or back to the [user guide](../user-guide.md).
