# Developer guide

How to build, test and run a development version of the extension after cloning. For using the extension see [getting-started.md](../getting-started.md) and [user-guide.md](../user-guide/README.md); for conventions and decisions see `CLAUDE.md`; known traps are in [pitfalls.md](pitfalls.md); milestone history is in [internal/milestones.md](../internal/milestones.md). Commands run from the repository root in PowerShell unless noted.

## Prerequisites
- Windows with **Visual Studio 2026** (version 18.x) and the *Visual Studio extension development* workload (VSSDK, the .NET Framework 4.7.2 targeting pack, MSBuild). The scripts default to the Community edition at `C:\Program Files\Microsoft Visual Studio\18\Community`; pass `-VsRoot` to `scripts\exp-reinstall.ps1` for another install.
- **.NET SDK 10** (`dotnet --version`) for the test projects and `dotnet pack`.
- **Odin** (a recent `dev-YYYY-MM` build) installed at `C:\Program Files\Odin\odin\dist\odin.exe`, or on `PATH`. Tier-2 and tier-3 tests and every build of an `.odinproj` need it. Odin's link step needs the MSVC environment, so run builds from a *Developer PowerShell for VS*.
- **git** and an SSH key only if you push. Git Bash cannot see the Windows ssh-agent; push from PowerShell (see `CLAUDE.md`, Git workflow rule 9).
- `ols` needs no install: a pinned copy ships inside the repo (`src/OdinVS/ols/ols.exe`, verified against `ols.pin` at build time).

## 1. Clone and check the toolchain
```powershell
git clone https://github.com/jake-pauls/odin-vs.git
cd odin-vs
dotnet --version
odin version    # or: & 'C:\Program Files\Odin\odin\dist\odin.exe' version, if odin is not on PATH
```

## 2. Build the local SDK package
`.odinproj` files use the `Odin.Sdk` MSBuild SDK, which is resolved from a local NuGet folder. The package is **not committed**; build it on every machine and after any change to `src/OdinVS.Build` or `src/OdinVS.Core`:
```powershell
scripts\pack-sdk.ps1
```
This writes `nuget\Odin.Sdk.<version>.nupkg` (the version already ends in `-local`; git-ignored, local only, never publish; see `nuget/README.md`). If you repack the same version, NuGet may keep serving its cached copy from `~\.nuget\packages\odin.sdk`; delete that folder yourself or bump `<Version>` in `src/OdinVS.Build/Odin.Sdk.csproj` (and the version in the samples).

## 3. Run the tests
```powershell
dotnet test tests/OdinVS.Core.Tests --filter "TestCategory!=RequiresToolchain"   # tier 1, no toolchain needed
dotnet test tests/OdinVS.Core.Tests                                              # adds tier 2 (ols protocol), needs Odin + bundled ols
dotnet test tests/OdinVS.Build.Tests                                             # tier 3, real odin, ~10 s
```
Tier 3 packs the SDK itself into a scratch folder each run, so it does not depend on step 2.

## 4. Build and run the extension in the Experimental instance
The Experimental ("Exp") instance is a separate VS profile, so a development build never touches your normal Visual Studio.
```powershell
scripts\exp-reinstall.ps1 -Bump -OpenFile tests\samples\LanguageServer\broken\main.odin
```
The script closes any running Exp, rebuilds `src/OdinVS` with VS's MSBuild, installs `OdinVS.vsix` into Exp (`/rootSuffix:Exp`) and opens the given file. `-Bump` raises the manifest patch version, which VS requires to accept a reinstall (see pitfalls). Check the result in Exp itself, not by the exit code; the log is `%LOCALAPPDATA%\OdinVS\odinvs.log`.

To build only (no install):
```powershell
& 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe' src\OdinVS\OdinVS.csproj /restore /p:Configuration=Debug /nologo /v:m
```
The VSIX lands in `src\OdinVS\bin\<Configuration>\net472\OdinVS.vsix`. Use `/nodeReuse:false` while changing the MSBuild task so a node does not lock the DLL.

## 5. Build an `.odinproj` from the command line
From a Developer PowerShell:
```powershell
msbuild tests\samples\HelloOdin\HelloOdin.odinproj /restore /t:Rebuild /p:Configuration=Debug
tests\samples\HelloOdin\bin\Debug\HelloOdin.exe
```
Requires step 2 (the samples restore `Odin.Sdk` through `tests/samples/nuget.config`).

## Layout
| Path | Purpose |
|---|---|
| `src/OdinVS` | The VSIX package (grammar, bundled ols, pkgdef with the `.odinproj` project-type registration, project and item templates) |
| `src/OdinVS.ProjectSystem` | The OdinVS package: tool-warning info bar (shown when the OdinProject UI context activates), the F5/Ctrl+F5 launch provider, and the subscriber that feeds each project's collections to ols (CPS itself is Visual Studio's) |
| `src/OdinVS.Debugger` | Concord debugger component: Odin slices, dynamic arrays, maps, unions, matrices, strings and Watch expressions (`a[i]`, `len`, `cap`, `m[key]`) |
| `src/OdinVS.LanguageServer` | LSP client for ols |
| `src/OdinVS.Core` | Logic with no VS references (error parser, argument builder, the `OdinBuild` task, reference resolver, collection tracker, diagnostic codes, map layout) |
| `src/OdinVS.Build` | Source of the `Odin.Sdk` NuGet package |
| `tests/` | `OdinVS.Core.Tests` (tiers 1-2), `OdinVS.Build.Tests` (tier 3), `samples/` |
| `scripts/` | `exp-reinstall.ps1`, `pack-sdk.ps1`, `bump-ols.ps1`, `new-stress-solution.ps1` |
| `spikes/` | Throwaway feasibility code, kept for reference |

## Before you commit
Test anything VS-visible in Exp first (Git workflow rule 12), keep the tests green, and update the table in [reference/diagnostics.md](../reference/diagnostics.md) (and [reference/project-properties.md](../reference/project-properties.md) for a project property) if you add a diagnostic. Never commit `nuget/*.nupkg`, and never push it to a package feed.
