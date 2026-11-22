# Getting started

From a clone to a running Odin project in Visual Studio. Allow about 15 minutes. There is no published release, so you build the extension yourself.

> **Experimental, personal software.** It is side-loaded into your own Visual Studio and has had one user. It was built with a lot of AI assistance (see the README). Read what you run: the build uses MSBuild targets, PowerShell scripts and a bundled `ols.exe` that come from this repository.

## 1. What you need

| Need | Why |
|---|---|
| Windows with Visual Studio 2026 (18.x) | The extension targets `[18.0,19.0)` only |
| The *Visual Studio extension development* workload | To build the extension (`.vsix`) |
| .NET SDK 10 | To build the extension and to pack the local `Odin.Sdk` package |
| [Odin](https://odin-lang.org) (tested with `dev-2026-10`), at `C:\Program Files\Odin\odin\dist\odin.exe` or on `PATH` | The compiler your projects build with |
| MSVC tools (in a *Developer PowerShell for VS*) | Odin's link step needs the MSVC environment for command-line builds; inside Visual Studio this is already set up |

The `ols` language server is bundled; you do not install it.

## 2. Build and install the extension

In PowerShell, from a folder of your choice:

```powershell
git clone https://github.com/jake-pauls/odin-vs.git
cd odin-vs
& 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe' src\OdinVS\OdinVS.csproj /restore /p:Configuration=Release /nologo /v:m
```

(Adjust the path if you installed another edition than Community.) Close Visual Studio, then double-click `src\OdinVS\bin\Release\net472\OdinVS.vsix`, choose *Install*, and reopen Visual Studio. Open any `.odin` file: you should see colorization.

## 3. Build the local `Odin.Sdk` package

`.odinproj` files use an MSBuild SDK called `Odin.Sdk`. It is **local only**: it is never published to nuget.org or any other feed, and the extension does not install it. Build it once per machine, and again after pulling changes:

```powershell
scripts\pack-sdk.ps1
```

This writes `nuget\Odin.Sdk.<version>.nupkg` (the version is in the file name, for example `0.5.1-local`).

## 4. Tell NuGet where it is

NuGet finds the package through a `nuget.config`. Create one **before** step 5, in the folder that will hold your Odin projects (or any parent folder), pointing at the `nuget` folder of your clone (an absolute path, or one relative to the `nuget.config`). `<clear />` drops nuget.org, which an Odin project does not need and which must not supply this package. If you open a repository someone else gave you, read any `nuget.config` in it first: it can add package sources.

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-odin" value="C:\path\to\odin-vs\nuget" />
  </packageSources>
</configuration>
```

`tests/samples/nuget.config` is a working example.

## 5. Create and build a project

1. In Visual Studio: *File > New > Project > **Odin Executable*** (or *Odin Dynamic Library (DLL)*), and create it inside the folder from step 4.
2. The first restore (when the project loads) pulls the package into `~\.nuget\packages`. The template writes the package version your build of the extension packs, so it matches step 3; if a project reports an SDK-resolution error, see [troubleshooting.md](troubleshooting.md).
3. *Build > Build Solution*. Errors and warnings appear in the Error List.
4. Press **F5** (or Ctrl+F5). A Debug build carries debug symbols, so breakpoints in `.odin` files bind.

If the project fails to load with an SDK-resolution error, steps 3 and 4 are not complete: see [troubleshooting.md](troubleshooting.md).

## 6. Build from the command line

From a Developer PowerShell:

```powershell
msbuild MyProject.odinproj /restore /t:Build
```

## Next

- [Projects in Visual Studio](user-guide/projects.md): open, create, build, toolchain warnings.
- [References between projects](user-guide/references.md), including linking an Odin DLL.
- [Debugging and the Odin value displays](user-guide/debugging.md).
- [Project properties](reference/project-properties.md) and [diagnostic codes](reference/diagnostics.md).
- [Known limitations](user-guide/limitations.md).
