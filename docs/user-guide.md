# Odin for Visual Studio: user guide

User-facing notes for people building `.odinproj` projects. Developer material lives in [dev-docs.md](dev-docs.md), `CLAUDE.md`, `docs/toolchain.md` and `docs/pitfalls.md`. These docs will be folded into a base `README.md` in a later milestone (see M4 in `milestones.md`).

Status: command-line builds work (M3). The sections below describe what exists today and are extended as milestones land.

## Installing the extension in Visual Studio

This installs a personal build into your normal Visual Studio 2026 (18.x) from a clone; there is no published release. To develop the extension in the separate Experimental instance instead, see [dev-docs.md](dev-docs.md).

What you get today: `.odin` syntax colorization, language-server diagnostics on save (squiggles and Error List) from the bundled `ols`, and command-line builds of `.odinproj` files. Opening an `.odinproj` in a solution, the Configuration Manager and F5 inside VS arrive with M4 and M5.

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
6. To build `.odinproj` files, run `scripts\pack-sdk.ps1` once (it creates the local `Odin.Sdk` package in `nuget\`), then follow the next section.

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

`Odin.Sdk` is a local-only package built by `scripts\pack-sdk.ps1` into `nuget/` (see `nuget/README.md`); point a `nuget.config` next to your project (or any parent folder) at that folder:

```xml
<configuration>
  <packageSources>
    <clear />
    <add key="local-odin" value="C:\path\to\odin-vs\nuget" />
  </packageSources>
</configuration>
```

`tests/samples/nuget.config` is a working example.

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
- Column numbers are 1-based; Odin's column 0 at end of file is shown as 1.

**Keep this table current.** Adding, renaming or removing any error, warning or other diagnostic code means updating it (and the constants in `src/OdinVS.Core/OdinBuildTask.cs` or the ols code mapper).
