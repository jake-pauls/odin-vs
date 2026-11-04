# odin-vs

Odin language support for **Visual Studio 2026** (18.x): `.odinproj` projects that sit in a normal `.sln`/`.slnx` next to C# and C++ projects, builds through the real `odin` compiler, and editor support from the bundled `ols` language server. Personal, side-loaded extension; there is no published release.

## What works today
- **Projects:** `.odinproj` files open in a solution, files show in Solution Explorer, Build/Rebuild/Clean work from the menu, compile errors and warnings reach the Error List (`ODIN0001`/`ODIN0002`), the Configuration Manager switches Debug/Release x64 together with the other projects, and File > New creates an Odin project.
- **Editor:** TextMate colorization, and `ols` diagnostics (squiggles on save), hover, completion and go to definition.
- **Command line:** `msbuild MyProject.odinproj /restore` builds without Visual Studio, using the `Odin.Sdk` MSBuild SDK.
- **Warnings:** an info bar appears when a solution holds an `.odinproj` and `odin.exe` or the bundled `ols.exe` is missing, or the installed `odin` is not the version the bundled `ols` was built for.
- **Not yet:** F5 and breakpoints (M5), project references and mixed Odin/C++/C# builds (M6).

## Requirements
Visual Studio 2026 with the *Visual Studio extension development* workload, the .NET SDK 10, and Odin (at `C:\Program Files\Odin\odin\dist\odin.exe` or on `PATH`). `ols` is bundled.

## Install and first project
1. Build the VSIX and install it into Visual Studio: [user-guide.md](docs/user-guide.md#installing-the-extension-in-visual-studio).
2. `Odin.Sdk` is a local-only package that is not installed by the extension. Build it once and point each project at it with a `nuget.config`: [Getting `Odin.Sdk` for a project](docs/user-guide.md#getting-odinsdk-for-a-project).
3. File > New > Project > **Odin Executable**, then build.

## Where to read more
| For | Read |
|---|---|
| Using the extension, project properties, configurations, diagnostic codes | [docs/user-guide.md](docs/user-guide.md) |
| Building, testing and running the extension from a clone | [docs/dev-docs.md](docs/dev-docs.md) |
| Decisions, conventions, architecture, git workflow | [CLAUDE.md](CLAUDE.md) |
| Verified toolchain facts (odin, ols) | [docs/toolchain.md](docs/toolchain.md) |
| Known traps and workarounds | [docs/pitfalls.md](docs/pitfalls.md) |
| Milestone checklists | [docs/milestones.md](docs/milestones.md) |

Third-party licenses (ols, the TextMate grammar): [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
