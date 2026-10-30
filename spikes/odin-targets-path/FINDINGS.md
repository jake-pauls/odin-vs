# `OdinTargetsPath` resolution (M0, finding 6)

Date: 2026-10-29. Branch `m0/odin-targets-path`.

## The problem
`.odinproj` files must import the Odin props/targets (build target, `OdinCompile` glob, configurations, CPS rules, debugger selection). The VSIX installs under a randomized directory, so a project cannot name the file by path, and `msbuild X.odinproj` from a shell cannot find a file that exists only inside a VSIX. The earlier spike imported `$(OdinTargetsPath)Odin.targets`, which nothing defined.

## Decision (user, 2026-10-29)
Use an **MSBuild SDK**: `<Project Sdk="Odin.Sdk/<version>">`. Command-line builds must work on machines that never ran VS with the extension (CI), so the SDK is distributed as a **NuGet package** (`PackageType=MSBuildSdk`), not through the VSIX.

## What was verified
- `pkg/Odin.Sdk.csproj` packs `Sdk/Sdk.props`, `Sdk/Sdk.targets` (the spike's `Odin.targets`) and `Sdk/Rules/*.xaml` into `feed/Odin.Sdk.0.1.0.nupkg` (`dotnet pack`, no compilation needed: `GenerateAssemblyInfo=false`; it still emits harmless CSC warnings).
- `nuget.config` with `<clear/>` and a single folder source `feed` resolves the SDK **with no network**.
- `sample/HelloOdin/HelloOdin.odinproj` is now just `<Project Sdk="Odin.Sdk/0.1.0"></Project>` (props set configurations, platforms and the `src\**\*.odin` glob; targets set capabilities, `Build`, rules, debugger flavor).
- **`dotnet msbuild`** and **VS MSBuild** (`MSBuild.exe`, Release) both resolved the SDK and ran the real `odin build`; `HelloOdin.exe` (+ PDB for Debug) appeared under `bin\<Configuration>\`.
- **VS Exp 18.11** opened `SdkSpike.sln` containing that project under our CPS type GUID: project loaded, Debug/x64 toolbar present, `src` folder shown; **Rebuild HelloOdin** printed `ODIN-STUB Clean` and `Build` lines in the Build output, then `1 succeeded`. The NuGet SDK resolver therefore works inside CPS design-time evaluation. (Needed the spike VSIX already installed in Exp for the project type.)
- The resolver extracted the SDK into the global NuGet cache (`~\.nuget\packages\odin.sdk\0.1.0`).

## Consequences and open points
- **Same-version repacks are cached**: bump the version (or clear the cache entry) on every change during development.
- A project needs a resolvable feed. Local folder feed via `nuget.config` next to the solution works; nuget.org publishing or a checked-in feed are the later choices (decide at M3; the VSIX can also ship the `.nupkg` and register a local source, unverified).
- Template-generated projects should emit `Sdk="Odin.Sdk/<pinned version>"` (replaces the absolute-path import in `OdinApp.odinproj`); the VSIX and SDK versions need pinning together.
- The MSBuild task assembly (`OdinVS.Core`, M3) will ship inside the package under `Sdk/` or `tools/`; watch for the NuGet cache locking it with node reuse.
- `odin.exe` location (separate open decision) is still hard-coded in `Sdk.targets`.
- Not tested: `.slnx`, opening the project with no feed configured (expect an SDK-not-found load error; the message was not captured), version ranges.
- Housekeeping: the Exp window opened for the check did not close when its X was clicked (likely a prompt behind it); close it manually.
