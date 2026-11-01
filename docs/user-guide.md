# Odin for Visual Studio: user guide

User-facing notes for people building `.odinproj` projects. Developer material lives in `CLAUDE.md`, `docs/toolchain.md` and `docs/pitfalls.md`.

Status: command-line builds work (M3). The sections below describe what exists today and are extended as milestones land.

## Building an `.odinproj`

A project uses the `Odin.Sdk` MSBuild SDK and lists its own source files:

```xml
<Project Sdk="Odin.Sdk/0.3.1-local">
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

`Odin.Sdk` is a local-only package (`feed/`, see `feed/README.md`); point a `nuget.config` at that folder. Output goes to `bin\<Configuration>\<ProjectName>.exe`. Debug builds add `-debug` and produce a `.pdb`; Release builds do not.

| Property | Default | Meaning |
|---|---|---|
| `OdinExe` | empty (search `PATH`, then `C:\Program Files\Odin\odin\dist\odin.exe`) | Path to `odin.exe` |
| `OdinPackageDir` | `src` | Directory passed to `odin build` |
| `OdinExtraArgs` | empty | Extra arguments appended to the `odin build` command |
| `Configurations` | `Debug;Release` | Configurations the project declares |
| `Platforms` / `Platform` | `x64` | Only x64 is supported |

## Configurations

`Configurations` is read like in a `.csproj`, so the command line accepts any name you list, for example `<Configurations>Debug;Release;Profile</Configurations>` and `msbuild /p:Configuration=Profile`. Only `Debug` adds `-debug`; other names build without it, so use `OdinExtraArgs` for anything else.

Visual Studio is more limited: it only learns about `Debug|x64` and `Release|x64` (see `docs/pitfalls.md`, "CPS reads `ProjectConfiguration` metadata unevaluated"). A custom name from `Configurations` is not shown in the Configuration Manager or the toolbar.

## Diagnostics

Build output uses MSBuild's canonical format, so Visual Studio shows entries in the Error List and double-click navigates to the line:

```
src\main.odin(6,11,6,17): error ODIN0001: Cannot convert '"text"' to 'int' ...
```

Odin does not give its errors codes, so odin-vs assigns them.

| Code | Severity | When | Notes |
|---|---|---|---|
| `ODIN0001` | error | Odin reported a compile error with a position | One code for every compile error; the message is Odin's own |
| `ODIN0002` | warning | Odin reported a compile warning | Not yet seen from a real build; assumes Odin marks warnings with type `warning` |
| `ODIN0100` | error | `odin build` failed (non-zero exit) but gave no error list, such as a link failure or crash | The message carries whatever Odin printed |
| `ODIN0101` | error | `odin.exe` was not found | Install Odin, add it to `PATH`, or set `OdinExe` |
| `ols.<code>` | error | A diagnostic from the ols language server while editing (for example `ols.checker`, `ols.Syntax`) | Appears on save; not part of the build |

Notes:
- A syntax error stops Odin before semantic checking, so a build shows only syntax errors until they are fixed.
- Column numbers are 1-based; Odin's column 0 at end of file is shown as 1.

**Keep this table current.** Adding, renaming or removing any error, warning or other diagnostic code means updating it (and the constants in `src/OdinVS.Core/OdinBuildTask.cs` or the ols code mapper).
