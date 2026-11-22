# Stress-test findings (M7, 2026-11-14)

The generated solutions themselves are not committed (they are big and rebuildable); this file is the record. Generate them with:

```powershell
scripts\pack-sdk.ps1
scripts\new-stress-solution.ps1 -Count 100 -Shape Tree  -OutputDirectory <new folder> -Measure
scripts\new-stress-solution.ps1 -Count 100 -Shape Chain -OutputDirectory <new folder> -Measure
scripts\new-stress-solution.ps1 -Count 100 -Shape Flat  -OutputDirectory <new folder> -Measure
scripts\new-stress-solution.ps1 -Count 300 -Shape Tree  -OutputDirectory <new folder> -Measure
```

Generated solutions default to `tests\samples\stress-<shape>-<count>\` (git-ignored; older runs used `stress-*\` in the repo root, also ignored). Only this file is tracked.

## What is generated
`Stress001` .. `StressNNN`, each an `.odinproj` with one package of its own plus a `main.odin` that prints the package's `value()`. Every reference is a **Source** reference, so a project compiles every package below it (all collections are transitive). Shapes:

| Shape | References | Depth | What it stresses |
|---|---|---|---|
| Tree | project `i` references `2i` and `2i+1` | about 7 for 100 projects | the realistic case |
| Chain | project `i` references `i+1` | 100 | worst case: the root passes 99 `-collection:` flags and compiles 100 packages |
| Flat | project 1 references every other project | 1 | one project with 99 direct references |

## Command line (VS MSBuild 18, `-m`, real odin `dev-2026-10-nightly:84bc3fc`, scratch NuGet cache, Debug x64)

| Shape / size | Cold build | No-op build | Touch one leaf, build | Clean |
|---|---|---|---|---|
| Tree / 100 | 14.8 s | 0.7 s | 2.5 s (the leaf's 6 ancestors and itself rebuild) | 0.7 s |
| Chain / 100 | 34.0 s | 1.2 s | 32.0 s (everything depends on the leaf) | 0.7 s |
| Flat / 100 | 14.4 s | 0.7 s | 1.4 s | 0.7 s |
| Tree / 300 | 45.6 s | 1.1 s | 3.9 s | 0.9 s |

Evaluating one project (`-getItem:OdinCompile`) takes 0.4 s. Every run produced all N executables (the script fails otherwise). Not a benchmark: one machine, one run each, no warm-up control beyond the first "SDK restore" step.

## Visual Studio (user, 2026-11-14, reported as pasted Output text)
`stress-chain\Stress.sln`, Build Solution, Debug x64: `Build: 100 succeeded or up-to-date, 0 failed, 0 skipped`, **36.2 s** (Output window, `Build completed ... took 36.196 seconds`). VS ordered the projects through the `ProjectReference` chain (Stress100 first, Stress001 last) and each project's odin command line carried the full transitive collection set (`Stress099` had 1, `Stress001` had 99). This matches the command-line Chain figure (34.0 s). Not reported: solution load time, memory and Solution Explorer behaviour (ols with 100 collections: see "Not done").

## What the stress test showed
- **Reference resolution is not the cost at this size.** No-op builds of 100 or 300 projects take about a second, so the up-to-date checks and the graph walk scale. The time is odin compiling the packages below each project.
- **Source references recompile everything below them**, so a deep chain is expensive on every change near its leaf (32 s) while a tree or a flat set stays cheap (1.4-2.5 s). That is by design (Odin has no separate compilation) and the reason `Inputs` include the transitive referenced sources. If a real solution has long chains, make the lower layers `Dll` projects with Link references instead (a Link reference does not recompile its sources).
- **The command line is long for deep chains**: `Stress001` in the Chain shape has 99 `-collection:` flags (about 12 KB of arguments). It worked with this odin; the Windows limit is 32 KB, so a chain of roughly 250 projects with long paths would hit it. Not tried.

## Findings about building solutions from the command line
1. `dotnet msbuild X.sln` runs **none** of the `.odinproj` projects (only `ValidateSolutionConfiguration`). Use VS MSBuild (`C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe`).
2. Pass `-p:Configuration=Debug -p:Platform=x64`. Without `Platform` the solution maps no project to a configuration and builds nothing, **silently** (exit code 0, "Build succeeded").
3. A solution `-restore` skips `.odinproj` projects (`NU1503`), so `Odin.Sdk` is not restored by it. Evaluate one project first (`msbuild <project> -restore -getProperty:OutDir`) to install the SDK into the NuGet cache; the generator script does this in its "SDK restore" step.
4. A solution calls each project's **default target**. `Sdk.props` did not set one, so the first target in `Sdk.targets` ran and nothing was built; `DefaultTargets="Build"` (PR `m6/mixed-sample`) fixes it. The numbers above were measured with that one-line change applied locally, so this branch depends on that PR.
5. In a generated solution file the GUID helper must not use `-f` with `{{...{0:D12}}}` (PowerShell mis-parses the closing braces and writes the literal `D12`, which makes MSBuild skip every project without an error).

## Not done
- Solution load time and memory in Visual Studio (needs the user in Exp; open a generated `Stress.sln` and note the Output window and Task Manager figures here).
- ~~ols with 100 collections~~ **Measured 2026-11-14 (tier-2 `OlsCollectionTests.OneHundredCollectionsInitializeAndResolveAnImportQuickly`, pinned ols dev-2026-10, real odin):** starting ols with 100 collections in `initializationOptions` and completing `initialize` took about 259 ms (single run), the first hover (an import through the 50th collection) 3 ms after `didOpen`, and the import resolved. `AHundredProjectsLoadingInOneBurstRaiseOneChange` (tier 1) shows 100 projects reporting in one burst raise one `CollectionsChanged` (the event that restarts ols; the restart itself is not tested). Not measured: the real CPS reporting cadence in Visual Studio, so how many restarts a load causes (500 ms quiet, 3 s cap). Not measured: ols checking time on a real 100-project solution (diagnostics only run on save) and the restart cost inside Visual Studio.
- A solution with mixed Odin, C++ and C# projects at this size.
