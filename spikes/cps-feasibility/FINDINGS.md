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
