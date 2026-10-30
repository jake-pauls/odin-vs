# CPS feasibility: Phase A findings

Date: 2026-10-29. Branch: `m0/cps-feasibility`. Method: reading `microsoft/VSProjectSystem` docs (via fetch agents), reading nuget.org package indexes (no downloads), and read-only inspection of the installed VS 18.11. No code was built. Everything below is tagged **Verified** (seen on this machine), **Docs** (read in the repo docs, which are VS 2015-era and undated), or **Unverified**.

## Verdict table

| Check | Verdict | Impossible? |
|---|---|---|
| A0 Authoring surface | Feasible with caveat | No |
| A1 Load | Feasible (strong evidence) | No |
| A2 File tree | Feasible with caveat | No |
| A3 Configurations | Likely feasible, unproven | No evidence of a blocker; the real test is B3 |
| A4 Add Item / Add Project | Feasible (standard mechanism) | No |
| A5 F5 stub | Feasible with caveat | No |

No check came back "impossible". The main risks are old docs and missing 18.x authoring packages, both addressable by Phase B probes.

## A0 Authoring surface
- **Verified:** CPS ships inside VS 18.11: `Microsoft.VisualStudio.ProjectSystem.dll`, `.Implementation.dll`, `.VS.dll`, `.VS.Implementation.dll`, all file version 18.11.22.40341, under `Common7\IDE\CommonExtensions\Microsoft\Project\`.
- **Verified (nuget.org index):** `Microsoft.VisualStudio.ProjectSystem`, `.SDK`, `.SDK.Tools`, `.Analyzers` all stop at **17.9.380** (stable) / 17.2.402-pre; **no 18.x** and no 17.10+. `.Managed` has a single 2.0.x version. So any authoring references are the 17.9 line.
- **Docs:** reference implementations listed are `dotnet/project-system` (the live C#/VB/F# system, public) and NuProj. Docs are VS 2015-era (mention VS 2015, "CPS API is not yet stable", .NET 4.6); no page mentions VS 2019/2022/2026.
- Caveat to test: whether the 17.9.380 reference assemblies compile and run against VS 18.11 (likely, as with the 17.14 VS SDK packages that loaded in 18.11, but unverified). Needs a package restore (ask).

## A1 Load
- **Verified:** VS 18.11 ships a minimal CPS project type, "Common Project System" (`.msbuildproj`), registered by a single pkgdef (`msbuildproj.pkgdef`): a `Projects\{type guid}` key with `@="ProjectFactory"`, `DisplayName`, `DisplayProjectFileExtensions`, `Package` (the CPS package GUID), `DefaultProjectExtension`, `PossibleProjectExtensions`, `ProjectTemplatesDir`, `Language(VsTemplate)`, `DisableAsynchronousSolutionLoad` (0, i.e. async load on), and `Capabilities` (`HandlesOwnReload;OpenProjectFile;EnableOnDemandExcludedFolderLoading`).
- **Docs:** the define-a-new-project-type page says a type can start from "as little as one small .pkgdef file" and is for projects with a drastically different schema or a new language (our case). The docs do not give GUIDs or capability lists.
- Plan: copy the `msbuildproj.pkgdef` pattern with our own type GUID and `.odinproj` extension. **Unverified:** that our GUID can reuse the same factory and Package GUIDs, and that a bare project opens from `.sln`/`.slnx` (B1).

## A2 File tree
- **Docs:** custom item types need a `ProjectItemsSchema.xaml` (`ContentType` + `ItemType` + `FileExtension` for `.odin`), wired in by a `<PropertyPageSchema Include=...>` item from a `.targets` file. Items whose type is not recognized are hidden. The tree provider interfaces are only for extra graft nodes and icons.
- **Not covered:** globbing and live updates when files appear on disk. `overview/globbing_behavior.md` exists in the docs index and has not been read; B2 tests it. Fallback: an ordinary MSBuild glob `<OdinCompile Include="src\**\*.odin" />` in the targets.

## A3 Configurations
- **Docs:** per-project configurations come from one of two capabilities: `ProjectConfigurationsDeclaredAsItems` (`ProjectConfiguration` items with `Configuration`/`Platform` metadata) or `ProjectConfigurationsInferredFromUsage`; a custom `IProjectConfigurationsService` can replace them. A fixed Debug/Release x64 set is directly supported by the first.
- **Not covered by the docs:** solution-configuration mapping and the top-bar dropdown with C#/C++ projects. CPS projects are normal solution members (SDK-style C# projects use CPS), so mapping is probably automatic, but this is **unverified** and is exactly the user's pass/fail criterion. B3 is the decisive test; do it early in Phase B.

## A4 Add Item and Add Project
- **Verified:** `msbuildproj.pkgdef` registers both template roots through the standard VS mechanism: `NewProjectTemplates\TemplateDirs\{package}\/1` (project templates, shown in the New Project window) and `Projects\{type guid}\AddItemTemplates\TemplateDirs\{package}\/1` (item templates, shown in Add > New Item). So our pkgdef can point at our own template folders.
- **Docs:** silent on `.vstemplate` contents; they only describe item types and a Preview SDK item template. Templates themselves are standard `.vstemplate` files in a VSIX; this is not CPS-specific. **Unverified:** the exact `ProjectType`/filtering rules so "Odin File" lists only for Odin projects; the shipped `Templates` folder was not present on disk to copy from.

## A5 F5 stub
- **Docs:** export a class deriving from `DebugLaunchProviderBase` with `[ExportDebugger]` and `[AppliesTo(<capability>)]`, override `CanLaunchAsync` and `QueryDebugTargetsAsync`, return `DebugLaunchSettings` with `LaunchDebugEngineGuid` (engine GUIDs live in `DebuggerEngines`). The page does not show executable path, arguments, working directory, or Ctrl+F5 (no-debug) handling, and is VS 2015-era with a sample bug.
- **Unverified (background):** `DebugLaunchSettings` has `Executable`, `Arguments`, `CurrentDirectory`, and `DebugLaunchOptions` has a no-debug flag. Confirm with the real API in B5.

## Docs still unread (cheap next reads)
`overview/globbing_behavior.md`, `overview/threading_rules.md`, `extensibility/reference_manager.md`, `extensibility/custom_reference_types.md`, `extensibility/IValidProjectReferenceChecker.md`, and the current `dotnet/project-system` source for how modern CPS types register capabilities.

## Suggested Phase B order
B1 Load (pkgdef from `msbuildproj.pkgdef`) -> B3 Configurations (decisive criterion) -> B2 File tree -> B5 F5 stub -> B4 Add Item -> B4-2 Add Project.

## Phase B1 (Load): first attempts, not yet passing

Setup: `OdinCpsSpike` VSIX (pkgdef only) installed into the Exp hive with `VSIXInstaller /rootSuffix:Exp`; `sample/CpsSpike.sln` opened from the Exp recent-solutions list.

1. **Own project type GUID, pkgdef only: "Unsupported".** VS showed "Review Project And Solution Changes: Unsupported ... `HelloOdin.odinproj`". The activity log from the first launch shows `odincps.pkgdef` was read, so the registration was processed; the likely cause is that no project factory object exists for our new GUID (CPS's package provides its factory only for the GUIDs it knows). **Verified:** unsupported. **Unverified:** the cause.
2. **Built-in `.msbuildproj` type, same project body: recognized but "(unloaded)".** A scratch probe solution using type GUID `{13B669BE-BB05-4DDF-9536-439F39A36129}` and `HelloOdin.msbuildproj` showed `Solution 'Probe' (0 of 1 project)` with `HelloOdin (unloaded)`. The CPS `ProjectPackage` ({3347BEE8-...}) loaded during the open, and the activity log contains no project-load error. So registration through the shipped type works as far as recognition, but a bare project body is not enough to load.
3. **Mistake worth remembering:** `devenv /log` takes a file name. I passed it the `.sln` path first, VS wrote its activity log over `CpsSpike.sln` (2.3 MB UTF-16 XML), and the solution "could not be opened". Restored from git with `git restore` (committed version intact). The log content is useful: it proved the pkgdef was loaded.

Next ideas, in order: (a) find why the `.msbuildproj` probe is unloaded (needed capabilities/imports; compare with VS's own `.msbuildproj` template if any, or strings in the CPS assemblies); (b) work out how a new GUID gets a factory (read `dotnet/project-system` registration, check CPS package `ProvideProjectFactory`/related attributes); (c) fall back to the MPFProj-style package-provided factory only for registration while keeping CPS for everything else (hybrid).

### B1 follow-up: why the built-in `.msbuildproj` probe is unloaded
- **Verified error text** (from Project > Reload Project in VS Exp, also in the Output window): *"The project file cannot be opened by the project system, because it is missing some critical imports or the referenced SDK cannot be found."* So CPS refuses a bare `<Project>` body; it expects specific imports or an SDK. The activity log had no entry for this, so the Output window and the Reload action are where load errors appear.
- Adding `Microsoft.Common.props` (via `$(MSBuildToolsPath)\...`, which was wrong: the file lives under `MSBuild\Current\`, giving "not imported ... due to the root not existing") and then `$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props` plus `$(MSBuildToolsPath)\Microsoft.Common.targets` **still** produced the same "critical imports" error. So the missing imports are something specific, not the generic MSBuild common targets. **Unverified candidates:** the design-time targets that provide `CompileDesignTime`/evaluation targets, an SDK-style `Sdk=` attribute, or a marker import CPS checks for.
- **CPS code facts (read from IL metadata and strings, no decompiler):** `ProjectPackage` ({3347BEE8-...}) carries no `ProvideProjectFactory` attribute; the CPS assemblies read the registry values `DefaultProjectExtension`, `PossibleProjectExtensions`, `Capabilities`, `ProjectFactoryPackage`, `OriginalProjectFactory` under `Projects\{guid}`; `PrefetchFactory` GUID {3998970A-...} is just a prefetcher helper type, not the project factory. The other shipped pkgdef-only CPS type (`codesharingproj.pkgdef`, `.shproj`) uses the identical pattern with Microsoft's own GUID.
- **Open questions for B1:** (1) what the "critical imports" are (read how a working CPS type such as `.shproj` or the C# SDK project sets them up); (2) whether a new GUID can ever get a factory through pkgdef alone, or CPS knows its GUIDs in code (our first attempt said Unsupported).
- **Tooling note:** `AsmInspect` (scratchpad, not in repo) reads custom attributes and user strings from a managed DLL with `System.Reflection.Metadata` and needs no NuGet packages; a real decompiler (ILSpy) would answer (1)/(2) quickly but means installing a tool (needs approval).

### B1 Load: PASSED (2026-10-30)
Decompiling the CPS assemblies with `ilspycmd` (output in the session scratchpad, not in the repo) explained both failures:
1. **"Unsupported" for our own GUID:** the public `ProjectTypeRegistrationAttribute` (in `Microsoft.VisualStudio.ProjectSystem.VS`) writes, besides `@="ProjectFactory"` and `Package`, the registry value **`ProjectFactoryPackage` = the CPS package GUID** {3347BEE8-D7A1-4082-95E4-38A439553CC2}. Our hand-written pkgdef lacked it. Adding it fixed registration. (The shipped `msbuildproj.pkgdef` omits it and works because those GUIDs are known to VS.) The attribute also sets `PrefetchFactory`, `Language(VsTemplate)`, `Capabilities`, template directories, async-load flags: it is the supported way to generate this registration from an assembly.
2. **"missing some critical imports or the referenced SDK cannot be found":** thrown by `PhysicalProjectTreeProvider.GetProjectRootProperties` when the project has **no `PropertyPageSchema` item with `<Context>BrowseObject</Context>`** (no BrowseObject catalog). It is unrelated to MSBuild Common imports. One tiny rule XAML in that context is enough (`sample/odin-build/Rules/OdinGeneral.BrowseObject.xaml`); with a catalog present but no project-level schema CPS synthesizes an empty rule. Also supplied: `OdinProjectItemsSchema.xaml` (Context `Project`) and the `OdinProject`/`ProjectConfigurationsDeclaredAsItems` capabilities, all from `sample/odin-build/Odin.targets` imported by the project.
- **Observed in VS Exp 18.11:** `Solution 'CpsSpike' (1 of 1 project)`, `HelloOdin` loaded, and the standard toolbar shows Debug / x64 configuration dropdowns. The project node has no children yet (file tree is B2).
- **Install notes:** bump the VSIX version for each rebuild; run `devenv /rootSuffix Exp /updateconfiguration` after installing so the pkgdef is merged.

### B2 File tree: PASSED (2026-10-30)
- With only the item-type schema (`OdinProjectItemsSchema.xaml`) the project node loaded but had **no children**. Adding a `File`-context rule (`OdinCompile.xaml`, `DataSource ItemType="OdinCompile"`) and a `BrowseObject`-context rule for the item type (`OdinCompile.BrowseObject.xaml`) made `src\main.odin` appear (modeled on VS's `None.xaml` / `None.BrowseObject.xaml` in `MSBuild\Microsoft\VisualStudio\Managed\`).
- A file added on disk while VS was open did **not** appear until the project capability **`UseFileGlobs`** (declared by C#'s `Microsoft.Managed.DesignTime.targets`) was added; after that, `live.odin` created from a shell appeared in about 10 seconds. So `<OdinCompile Include="src\**\*.odin"/>` globs track the disk live.
- `sample/odin-build/Odin.targets` now declares: `OdinProject`, `ProjectConfigurationsDeclaredAsItems`, `UseFileGlobs`, and the four schemas (project items, BrowseObject general, OdinCompile File, OdinCompile BrowseObject).

### B3 Configurations: PASSED (2026-10-30)
Solution `sample/MixedSpike.sln` = `HelloOdin.odinproj` (our CPS type) + `CsApp.csproj` (SDK-style, x64) + `CppLib.vcxproj` (static library), all mapped Debug|x64 and Release|x64.
- All three projects loaded in VS Exp 18.11 (`Solution 'MixedSpike' (3 of 3 projects)`).
- **Build > Configuration Manager** lists CppLib, CsApp and HelloOdin; selecting solution configuration **Release** switched all three rows to Release|x64, and the **toolbar dropdown** then showed Release. Switching back to Debug through the toolbar dropdown showed Debug for all three in the Configuration Manager. The dropdown also offers `<New...>`/`<Edit...>`, so user-defined solution configurations are available; the Odin project would need matching `ProjectConfiguration` items for any new name.
- **Odin project only needs** `ProjectConfiguration` items (`Configuration`/`Platform` metadata) plus the `ProjectConfigurationsDeclaredAsItems` capability and `Configurations`/`Platforms` properties; the solution file's per-project mapping lines do the rest.
- **Build through VS works:** with stub `Build`/`Clean`/`Rebuild` targets in `odin-build/Odin.targets`, **Build > Build HelloOdin** printed `ODIN-STUB Build: Configuration=Debug Platform=x64 Project=HelloOdin` in the Output window and `Build: 1 succeeded`. Command-line `msbuild` printed the matching Debug and Release lines. The Release build from within VS was not separately observed.
- Note: VS showed `CppLib (Visual Studio 2010)` for the hand-written vcxproj (default toolset); irrelevant to the spike.
