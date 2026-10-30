# `OdinTargetsPath` resolution (M0, finding 6)

Date: 2026-10-29. Branch `m0/odin-targets-path`.

## The problem
`.odinproj` files must import the Odin props/targets (build target, `OdinCompile` glob, configurations, CPS rules, debugger selection). The VSIX installs under a randomized directory, so a project cannot name the file by path, and `msbuild X.odinproj` from a shell cannot find a file that exists only inside a VSIX. The earlier spike imported `$(OdinTargetsPath)Odin.targets`, which nothing defined.

## Decision (user, 2026-10-29)
Use an **MSBuild SDK**: `<Project Sdk="Odin.Sdk/<version>">`. Command-line builds must work on machines that never ran VS with the extension (CI), so the SDK is distributed as a **NuGet package** (`PackageType=MSBuildSdk`), not through the VSIX.

## What was verified
- `pkg/Odin.Sdk.csproj` packs `Sdk/Sdk.props`, `Sdk/Sdk.targets` (the spike's `Odin.targets`) and `Sdk/Rules/*.xaml` into `feed/Odin.Sdk.0.1.0.nupkg` (`dotnet pack`, no compilation needed: `GenerateAssemblyInfo=false`; it still emits harmless CSC warnings).
- `nuget.config` with `<clear/>` and a single folder source `feed` resolves the SDK **with no network**.
- `sample/HelloOdin/HelloOdin.odinproj` started as `<Project Sdk="Odin.Sdk/0.1.0"></Project>`, with props that also supplied the `src\**\*.odin` glob; that part was reverted, see "Revision 2026-10-30" below (targets set capabilities, `Build`, rules, debugger flavor).
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
- Housekeeping: the Exp window ignored its X button but closed via File > Exit.

## Approval and local-only safeguards (2026-10-29)
The user confirmed the problem framing and the SDK approach. The package stays local until the user says otherwise:
- `nuget.config` has `<clear/>` plus the single `feed` folder source, so restore never contacts nuget.org.
- `nuget.config` sets `defaultPushSource` to `feed`; a bare `dotnet nuget push` (run from this folder) was observed to target the local folder, not nuget.org. This is a guard rail, not a lock: `dotnet nuget push -s <url>` still works if someone passes a source and an API key, and none is configured on this machine (not verified beyond this repo).
- `feed/` is git-ignored, so the `.nupkg` is not committed.
- Never run `dotnet nuget push` with an explicit source for `Odin.Sdk` without the user's go-ahead (also covered by the user-level "ask before network requests" rule).

## Revision 2026-10-30: the project owns its file list (user decision)
**Decision:** `.odinproj` authors decide what goes into their project. The SDK does **not** add an `OdinCompile` glob (or any other item). Every project lists its own, e.g. `<OdinCompile Include="src\**\*.odin" />`; the project template emits that line. **Intent:** the `.odinproj` stays the readable source of truth (a hard requirement), with no hidden defaults about which files compile, so what you see in the file is what builds. The earlier draft had moved the glob into `Sdk.props` only to make the sample a one-liner; that was my choice, not a requirement, and it is reverted.

**Configurations follow `Microsoft.NET.Sdk`.** Checked in the installed .NET SDK 10.0.403 (`Microsoft.NET.Sdk.props`): `Configurations` defaults to `Debug;Release` only when the project has not set it, `Configuration` to `Debug`, and `Platforms` to `AnyCPU`. Ours is the same with `x64`: a project with no configuration settings gets Debug and Release x64, and a project that sets `<Configurations>` gets its own list.
- Defaults live in `Sdk.props` (conditional properties only). The `ProjectConfiguration` items CPS needs are built in `Sdk.targets` from the final `Configurations` value, because the props import runs before the project body and would otherwise ignore the project's list.
- Verified by `dotnet msbuild -getItem:ProjectConfiguration`: default project gives `Debug|x64` and `Release|x64`; a project with `<Configurations>Debug;Release;Profile</Configurations>` gives all three. Real `odin build` still works from the sample. **Not re-run in VS Exp** after this change (the item set is the same for the default case).
- **Limits found:** item batching across two lists is not evaluated in static item groups (it produced literal `%(...)` text), and CPS reads static evaluation, so only **one platform** (`x64`) is supported for now. `-p:Configurations=A%3BB` on the command line is escaped into one value; set the property in the project instead.
- Package version is now `0.1.4` (each repack needed a new version because of the NuGet cache).
