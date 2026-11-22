# odin-vs

Odin language support for **Visual Studio 2026** (18.x): `.odinproj` projects that sit in a normal `.sln`/`.slnx` next to C# and C++ projects, builds through the real `odin` compiler, editor support from the bundled `ols` language server, and debugger displays for Odin values.

> **Experimental, personal software.** This is a side-loaded extension built for one person's own use. There is no published release and no Marketplace listing, and the `Odin.Sdk` MSBuild package it needs is **local only** (it is built on your machine and never published). It is provided as is, under the MIT license, with no support promised. Building, debugging or even opening a solution runs MSBuild and your code, so treat untrusted `.odinproj` and `.odin` files like any untrusted project. Not affiliated with the Odin project, `ols`, or Microsoft.

> **Built with AI assistance.** This project began as an experiment in an AI-assisted workflow. I set the requirements, made the design decisions and reviewed the results, but most of the code was written by Claude (Anthropic's AI model). The commit history and pull requests show that process. Expect the quality, and the bugs, of that approach: review the code before you trust it.

## What works today
- **Projects:** `.odinproj` files open in a solution, files show in Solution Explorer, Build/Rebuild/Clean work from the menu, the Configuration Manager switches Debug/Release x64 together with C# and C++ projects, and *File > New* creates an Odin executable or DLL project.
- **Build:** compile errors and warnings reach the Error List (`ODIN0001`/`ODIN0002`); `msbuild MyProject.odinproj /restore` builds without Visual Studio using the `Odin.Sdk` MSBuild SDK.
- **Debugging:** F5/Ctrl+F5 and breakpoints in `.odin` files, plus readable Locals, Watch and data tips for slices, dynamic arrays, maps, strings, unions and matrices, and Watch expressions such as `len(x)`, `x[i]` and `m[key]`.
- **References:** project references between Odin projects (source), linking an Odin DLL, and mixed Odin/C++/C# solutions.
- **Editor:** TextMate colorization, and `ols` diagnostics (squiggles on save), hover, completion and go to definition.
- **Warnings:** an info bar appears when `odin.exe` or the bundled `ols.exe` is missing, or the installed `odin` is not the version the bundled `ols` was built for.

## Known limits
Windows and x64 only; Visual Studio 2026 (18.x) only; tested with Odin `dev-2026-10`. Static libraries (`OdinOutputType` `Lib`) do not link with that Odin, and the Autos window is empty for Odin. Full list: [Known limitations](docs/user-guide/limitations.md).

## Quick start
1. Install the prerequisites: Visual Studio 2026 with the *Visual Studio extension development* workload, .NET SDK 10, and [Odin](https://odin-lang.org) (at `C:\Program Files\Odin\odin\dist\odin.exe` or on `PATH`).
2. Clone, then build the extension with Visual Studio's MSBuild (`MSBuild.exe src\OdinVS\OdinVS.csproj /restore /p:Configuration=Release`), close Visual Studio and run `src\OdinVS\bin\Release\net472\OdinVS.vsix`.
3. Build the local SDK package with `scripts\pack-sdk.ps1`, then add a `nuget.config` that points NuGet at the clone's `nuget` folder (an Odin project cannot load without it).
4. *File > New > Project > **Odin Executable***, build, press F5.

The exact commands and the `nuget.config` contents are in [Getting started](docs/getting-started.md).

## Documentation
| For | Read |
|---|---|
| Install and first project | [docs/getting-started.md](docs/getting-started.md) |
| Using the extension | [docs/user-guide.md](docs/user-guide.md) |
| Project properties, configurations, diagnostic codes | [docs/reference/](docs/reference/project-properties.md) |
| Something is broken | [docs/troubleshooting.md](docs/troubleshooting.md) |
| Building and testing the extension from a clone | [docs/dev-docs.md](docs/dev-docs.md) |
| Everything else (index) | [docs/README.md](docs/README.md) |

Sample projects to try are under [tests/samples](tests/samples).

## License
[MIT](LICENSE). Bundled third-party components (the `ols` language server and the TextMate grammar) keep their own licenses: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). The repository contains a prebuilt, unsigned `ols.exe` (its hash is pinned in `src/OdinVS/ols/ols.pin`); read the scripts under `scripts/` before running them.
