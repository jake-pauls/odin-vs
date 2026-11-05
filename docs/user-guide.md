# Odin for Visual Studio: user guide

User-facing notes for people building `.odinproj` projects. The overview and install entry point is the root [README.md](../README.md). Developer material lives in [dev-docs.md](dev-docs.md), `CLAUDE.md`, `docs/toolchain.md` and `docs/pitfalls.md`.

Status: `.odinproj` projects open in a solution, build from VS and the command line, and show errors in the Error List (M3, M4). F5, Ctrl+F5 and breakpoints in `.odin` files work (M5); in Locals, slices and dynamic arrays show only raw `data`/`len` (see M5.5); references arrive with M6.

## Installing the extension in Visual Studio

This installs a personal build into your normal Visual Studio 2026 (18.x) from a clone; there is no published release. To develop the extension in the separate Experimental instance instead, see [dev-docs.md](dev-docs.md).

What you get today: `.odin` syntax colorization, language-server diagnostics on save (squiggles and Error List) from the bundled `ols`, `.odinproj` projects in a solution, and command-line builds. Odin projects in a solution (tree, Build/Clean/Rebuild, Configuration Manager, File > New) work from M4; F5 and Ctrl+F5 launch the built program under the debugger (M5).

1. Install the prerequisites: Visual Studio 2026 with the *Visual Studio extension development* workload, the .NET SDK 10, and Odin (at `C:\Program Files\Odin\odin\dist\odin.exe` or on `PATH`). `ols` is bundled.
2. Clone and build the extension from a PowerShell prompt in the clone:
   ```powershell
   git clone git@github.com:jake-pauls/odin-vs.git
   cd odin-vs
   & 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe' src\OdinVS\OdinVS.csproj /restore /p:Configuration=Release /nologo /v:m
   ```
   The installer is `src\OdinVS\bin\Release\net472\OdinVS.vsix`.
3. Close Visual Studio, then double-click `OdinVS.vsix` and choose *Install*. Reopen Visual Studio and open any `.odin` file.
4. To update after pulling changes: rebuild, close Visual Studio, and run the new `.vsix`. If VS reports the same version as already installed, raise the `Version` in `src\OdinVS\source.extension.vsixmanifest` first.
5. To remove it: *Extensions > Manage Extensions > Installed > Odin for Visual Studio > Uninstall*, then restart VS.
6. To build `.odinproj` files you also need the local `Odin.Sdk` package; see "Getting `Odin.Sdk` for a project" below.

## Building an `.odinproj`

A project uses the `Odin.Sdk` MSBuild SDK and lists its own source files:

```xml
<Project Sdk="Odin.Sdk/0.3.3-local">
  <ItemGroup>
    <OdinCompile Include="src\**\*.odin" />
  </ItemGroup>
</Project>
```

Build from a Developer shell (odin's link step needs the MSVC environment):

```
msbuild MyProject.odinproj /restore /t:Build
msbuild MyProject.odinproj /restore /t:Rebuild /p:Configuration=Release
```

### Getting `Odin.Sdk` for a project

`Odin.Sdk` is local-only: it is never published to nuget.org or any shared feed, and the extension does not install it. Each project (or a parent folder shared by several) points NuGet at your local copy:

1. Build the package once per machine, and again after pulling changes to `src\OdinVS.Build` or `src\OdinVS.Core`. It is written to `nuget\` (see `nuget\README.md`):
   ```
   scripts\pack-sdk.ps1
   ```
2. Next to the `.odinproj` (or in any parent folder, to cover several projects) create a `nuget.config` that points at that folder. `<clear />` drops nuget.org, which is not needed:
   ```xml
   <?xml version="1.0" encoding="utf-8"?>
   <configuration>
     <packageSources>
       <clear />
       <add key="local-odin" value="C:\path\to\odin-vs\nuget" />
     </packageSources>
   </configuration>
   ```
3. Make sure the project's `Sdk` attribute names the version you packed: the file `nuget\Odin.Sdk.<version>.nupkg` gives it, e.g. `Odin.Sdk/0.3.3-local`.
4. Build or reload the project. The first restore pulls the SDK into `~\.nuget\packages`; if you repack the same version, delete `~\.nuget\packages\odin.sdk` (or bump the version) or the old copy keeps being used.

`tests/samples/nuget.config` is a working example. A project created from the Odin templates in Visual Studio needs steps 2 and 3 as well; until they are done it fails to load with an SDK-resolution error.

Output goes to `bin\<Configuration>\<ProjectName>.exe` unless overridden; see [Project properties and items](#project-properties-and-items). Debug builds add `-debug` and produce a `.pdb`; Release builds do not.

## Project properties and items

Everything an `.odinproj` can set, with its default. Properties are plain MSBuild properties: set them in the project's own `PropertyGroup` to override the default. Compiler options are deliberately not modelled as properties; pass any `odin` flag through `OdinExtraArgs`. **This list is kept current: it is updated whenever a project property or item is added or wrapped.**

| Property | Default | Meaning |
|---|---|---|
| `Configurations` | `Debug;Release` | Configurations the project declares (see [Configurations](#configurations) for Visual Studio) |
| `Platforms` | `x64` | Platforms the project declares; only x64 is supported |
| `Configuration` | `Debug` | The configuration to build when none is passed (`/p:Configuration=...`) |
| `Platform` | `x64` | The platform to build |
| `OdinExe` | empty (search `PATH`, then `C:\Program Files\Odin\odin\dist\odin.exe`) | Path to `odin.exe` |
| `OdinPackageDir` | `src` | Directory passed to `odin build` (relative to the project, or absolute) |
| `OdinExtraArgs` | empty | Extra arguments appended to the `odin build` command: the place for any `odin` compiler option (`-o:speed`, `-define:X=1`, `-vet`) |
| `OdinDebug` | `true` in Debug, `false` otherwise | Adds `-debug` (and produces the `.pdb`). A default you can override, e.g. `<OdinDebug>true</OdinDebug>` for a Profile configuration |
| `OutDir` | `bin\<Configuration>\` | Output directory (relative to the project, or absolute) |
| `TargetName` | project name | Output file name without extension |
| `LocalDebuggerCommandArguments` | empty | Arguments passed to the program on F5 and Ctrl+F5 |
| `LocalDebuggerWorkingDirectory` | the project directory | Working directory on F5 and Ctrl+F5 (relative to the project, or absolute) |
| `TargetPath` | `<OutDir><TargetName>.exe` | Full output path (relative to the project, or absolute); the `.pdb` is written next to it with the same name |

| Item | Purpose |
|---|---|
| `OdinCompile` | The project's own source files, e.g. `<OdinCompile Include="src\**\*.odin" />`. The SDK adds none; the `.odinproj` lists them. Changing one rebuilds |
| `OdinCollection` | An `-collection:name=path` passed to `odin`: `<OdinCollection Include="name"><Path>dir</Path></OdinCollection>` |
| `ProjectConfiguration` | Declares a configuration to Visual Studio; see [Configurations](#configurations) |

Incremental builds look at the `OdinCompile` files and the `.odinproj`, so changing a property only with `/p:` on the command line does not trigger a rebuild; use the Rebuild target.

## Configurations

`Configurations` is read like in a `.csproj`, so the command line accepts any name you list, for example `<Configurations>Debug;Release;Profile</Configurations>` and `msbuild /p:Configuration=Profile`. Only `Debug` adds `-debug`; other names build without it, so use `OdinExtraArgs` for anything else.

Visual Studio learns a project's configurations from `ProjectConfiguration` items, not from the `Configurations` property. The SDK declares `Debug|x64` and `Release|x64` for you, so a custom name listed only in `Configurations` builds from the command line but is **not** shown in the Configuration Manager or the toolbar.

To make a custom configuration appear in VS, declare literal items in the `.odinproj` for **every** configuration the project should show, including Debug and Release (verified in VS Exp 18.11: with only a `Profile` item, VS offered just `Profile`; with all three, it offered all three and a Profile solution configuration built into `bin\Profile`):

```xml
<PropertyGroup>
  <Configurations>Debug;Release;Profile</Configurations>
</PropertyGroup>
<ItemGroup>
  <ProjectConfiguration Include="Debug|x64"><Configuration>Debug</Configuration><Platform>x64</Platform></ProjectConfiguration>
  <ProjectConfiguration Include="Release|x64"><Configuration>Release</Configuration><Platform>x64</Platform></ProjectConfiguration>
  <ProjectConfiguration Include="Profile|x64"><Configuration>Profile</Configuration><Platform>x64</Platform></ProjectConfiguration>
</ItemGroup>
```

The values must be plain text: VS reads them without evaluating `$(...)` (see `docs/pitfalls.md`). A solution configuration of the same name still has to be created in the Configuration Manager (or the `.sln`), and each project mapped to it; a project that does not declare that name is skipped when that solution configuration builds.

## Projects in Visual Studio

- **Open or add a project:** open a `.sln`/`.slnx` that lists `.odinproj` files, or use *File > Open > Project/Solution* on an `.odinproj`; *Add > Existing Project* also works. The project node lists the files its `OdinCompile` items name (a glob such as `src\**\*.odin` tracks files added or removed on disk).
- **Create one:** *File > New > Project > Odin Executable* (or *Add > New Project* in a solution) generates `<Name>.odinproj` and `src\main.odin`. The generated project names `Odin.Sdk` with the version this build packs; it only restores once NuGet can reach the package, see [Getting `Odin.Sdk` for a project](#getting-odinsdk-for-a-project). *Add > New Item > Odin File* adds a source file.
- **Build:** *Build > Build/Rebuild/Clean Solution* (or the project entries) run the same `odin build` command as the command line; errors and warnings go to the Error List and double-click opens the line. The toolbar and *Build > Configuration Manager* switch Debug/Release x64 for every project in the solution; output goes to `bin\<Configuration>`.
- **If a different "Odin CPS Spike" extension is installed** (an early development build) uninstall it; it registers the same project type.
- **Warnings about the toolchain:** when a solution holds an `.odinproj`, an info bar at the top of the window appears if `odin.exe` is not found, the bundled `ols.exe` is missing, or the installed odin is not the version the bundled ols was built for (messages say what to fix; *Open log* opens `%LOCALAPPDATA%\OdinVS\odinvs.log`). The check runs each time a solution containing an `.odinproj` is opened or reloaded, and a new result replaces the previous bar.

## Diagnostics

Build output uses MSBuild's canonical format, so Visual Studio shows entries in the Error List and double-click navigates to the line:

```
src\main.odin(6,11,6,17): error ODIN0001: Cannot convert '"text"' to 'int' ...
```

Odin does not give its errors codes, so odin-vs assigns them.

| Code | Severity | When | Notes |
|---|---|---|---|
| `ODIN0001` | error | Odin reported a compile error with a position | One code for every compile error; the message is Odin's own |
| `ODIN0002` | warning | Odin reported a compile warning, for example use of a `@(deprecated)` procedure | Does not fail the build; the exe is still produced |
| `ODIN0100` | error | `odin build` failed (non-zero exit) but gave no error list, such as a link failure or crash | The message carries whatever Odin printed |
| `ODIN0101` | error | `odin.exe` was not found | Install Odin, add it to `PATH`, or set `OdinExe` |
| `ODINPKG1` | warning | Packing `Odin.Sdk` with `scripts\pack-sdk.ps1` | Reminder that the package is local only and must not be published; not a build diagnostic |
| `ols.<code>` | error | A diagnostic from the ols language server while editing (for example `ols.checker`, `ols.Syntax`) | Appears on save; not part of the build |

Notes:
- A syntax error stops Odin before semantic checking, so a build shows only syntax errors until they are fixed.
- In the editor, ols diagnostics with warning severity (such as a call to a `@(deprecated)` procedure) draw the editor's warning squiggle, which Visual Studio colours green by default; errors are red.
- Column numbers are 1-based; Odin's column 0 at end of file is shown as 1.

**Keep this table current.** Adding, renaming or removing any error, warning or other diagnostic code means updating it (and the constants in `src/OdinVS.Core/OdinBuildTask.cs` or the ols code mapper).
