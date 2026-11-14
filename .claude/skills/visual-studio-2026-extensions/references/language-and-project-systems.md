# Language support, project systems, and LSP

Notes from planning a full language-support extension (Odin: grammar, language server, custom project type, MSBuild build, debugging). Each claim is tagged **Verified** (observed on a Visual Studio 2026 18.x machine), **Sourced** (read in Microsoft docs, summarized), or **Unverified** (background knowledge; confirm with a spike before relying on it).

## Pick the model by the heaviest feature

- **Custom project types, language servers hosted with `ILanguageClient`, and anything built on MEF or COM interfaces need the classic VSSDK.** The new `VisualStudio.Extensibility` model has no project-system API. *Unverified for current packages; check the SDK before assuming.*
- **The "hybrid" VSIX (VSSDK + new model) runs the new-model code in-proc.** That forfeits the out-of-proc crash isolation that is the main reason to use the new model. If a VSSDK package is needed anyway, a hybrid adds hosting complexity for little gain; prefer VSSDK-only for language support. *Sourced/reasoned, not tested.*
- Language servers run as separate processes either way, so a server crash does not take down VS regardless of model.

## Project systems: what they are and the options

A project system is what makes a project file load in a solution: the Solution Explorer tree, Build/Clean/Rebuild, the Configuration Manager, Add Item/Add Reference, F5 launch, and file membership. Without one, VS cannot open the project type.

| Option | What it is | Fits when | Costs |
|---|---|---|---|
| **MPFProj** (Managed Package Framework for Projects) | C# source library wrapping the legacy COM project interfaces (`IVsHierarchy`, `IVsProject`, ...). You subclass `ProjectNode`/`FileNode` and hand-write the tree, configuration provider, MSBuild hosting. In-proc, VSSDK only. | A few projects; you want readable example code (ZigVS is a real one). | Legacy, synchronous and UI-thread heavy (poor at hundreds of projects, **Unverified**), hand-written plumbing; the 2015 MS blog calls a full MPFProj system "many engineer-months" (**Sourced**). |
| **CPS** (Common Project System) | MEF-based framework behind SDK-style .NET projects. You declare capabilities, describe properties as XAML rules, and export providers (launch/debug, item templates, reference types, up-to-date check). Tree comes from MSBuild evaluation. | Many projects, Configuration Manager from MSBuild `Configurations`/`Platforms` (**Unverified**), modern async load. | Steep learning curve, sparse docs, opaque MEF composition failures. Docs: `microsoft/VSProjectSystem` repo (`doc/Index.md`), breaking-change notes end at VS 2019 (**Sourced**). |
| **Flavoring an existing project type** | Extend a CPS project (e.g. C#) rather than defining a new one. | You can live inside an existing project format. | Locked to that project's model. |
| **Open Folder** | No project file; `launch.vs.json` and `tasks.vs.json` supply build/run/debug. | Single-language, small solutions, no solution integration needed. | No Configuration Manager, no mixed-language solution workflow, no project references. |

Decision inputs to collect before choosing (get the user's answers first):
1. Must it live in a `.sln` next to C#/C++ projects?
2. Which Solution Explorer operations: Add Project, Add Existing Project, Add Item, Add Reference?
3. Is the Configuration Manager required? Property pages?
4. How many projects: 10-20 typical, 100+ possible?
5. Is the project file the source of truth (plain MSBuild XML)?
6. How do references map to the language's own model (see below)?

Practical advice:
- **Spike before committing** (see Process). A project system is the largest single cost; run a timeboxed prototype of the minimum slice (load, tree, configurations, Add Item, F5 stub) and record time per sub-goal.
- Add Project / Add Existing Project are the same work in every option: a registered project type GUID, a project factory, and templates.
- Add Reference (the Reference Manager dialog) is likely the hardest piece in any option (**Unverified**).
- CPS exposes `IValidProjectReferenceChecker` as a hook for rejecting invalid or circular references, and `IBuildUpToDateCheckProvider` for fast up-to-date checks at scale (**Sourced**).

## CPS: what was learned about defining a custom project type (VS 18.11)

- **CPS ships inside Visual Studio.** `Microsoft.VisualStudio.ProjectSystem*.dll` (file version 18.11.x) live under `Common7\IDE\CommonExtensions\Microsoft\Project\`. *Verified.* The public NuGet authoring packages (`Microsoft.VisualStudio.ProjectSystem`, `.SDK`, `.SDK.Tools`, `.Analyzers`) stop at **17.9.380**; there is no 18.x. *Verified (nuget.org index).* The `microsoft/VSProjectSystem` docs are VS 2015-era and undated; `dotnet/project-system` is the live reference implementation. *Docs.*
- **Best template to copy is on disk:** `Common7\IDE\CommonExtensions\Microsoft\Project\msbuildproj.pkgdef` registers a complete minimal CPS project type (`.msbuildproj`) in about 20 lines. *Verified.* It sets, under `[$RootKey$\Projects\{type guid}]`: default value `ProjectFactory`, `DisplayName`, `DisplayProjectFileExtensions`, `Package` (the CPS package GUID), `DefaultProjectExtension`, `PossibleProjectExtensions`, `ProjectTemplatesDir`, `Language(VsTemplate)`, `DisableAsynchronousSolutionLoad`, `DisableAsynchronousProjectTreeLoad`, `PrefetchFactory`, and `Capabilities` (`HandlesOwnReload;OpenProjectFile;EnableOnDemandExcludedFolderLoading`). The same file registers `NewProjectTemplates\TemplateDirs` (New Project window) and `Projects\{type guid}\AddItemTemplates\TemplateDirs` (Add > New Item). Reuse the CPS Package GUID, use your own project type GUID.
- **Capabilities are the switchboard.** CPS components apply through `[AppliesTo("capability expression")]`, and a project advertises capabilities with `<ProjectCapability Include="..."/>` items in the project file or imported targets (C#'s `Microsoft.Managed.DesignTime.targets` is a good list of what the .NET system declares: `UseFileGlobs`, `LaunchProfiles`, `DependenciesTree`, `ProjectImportsTree`, ...). Per-project configurations come from `ProjectConfigurationsDeclaredAsItems` (`ProjectConfiguration` items with `Configuration`/`Platform` metadata) or `ProjectConfigurationsInferredFromUsage`. *Docs.*
- **File tree:** a custom item type shows in Solution Explorer only if registered through a `ProjectItemsSchema.xaml` (`ContentType`, `ItemType`, `FileExtension`) pulled in by a `<PropertyPageSchema Include=...>` item. The tree-provider interfaces are for extra graft nodes and icons, not for listing files. *Docs.* Live disk updates for globbed items are untested.
- **Debug launch:** `DebugLaunchProviderBase` with `[ExportDebugger]` and `[AppliesTo(capability)]`, returning `DebugLaunchSettings` (engine GUID from `DebuggerEngines`). *Docs; later verified in VS 18.11 Exp: read `TargetPath`, working directory and arguments from `ConfiguredProject.Services.ProjectPropertiesProvider.GetCommonProperties()` instead of hard-coding, and add a `MefComponent` VSIX asset for the assembly holding the provider; see `references/pitfalls.md` ("Debugging").*
- **Check for what ships before inventing:** search the VS install for `*.pkgdef` and `*.targets` mentioning `ProjectSystem`/`ProjectCapability`; VS's own project types are the most current documentation.

### Verified recipe: a minimal custom CPS project type (VS 18.11, all *Verified* in VS Exp)
A `.odinproj`-style project loaded, showed live files, followed the Configuration Manager next to C#/C++ projects, built via its own target, launched under F5, and had Add Item / Add Project templates. What each piece needed:
1. **Registration (pkgdef):** `[$RootKey$\Projects\{your type GUID}]` with `@="ProjectFactory"`, **`ProjectFactoryPackage` and `Package` both = `{3347BEE8-D7A1-4082-95E4-38A439553CC2}`** (the CPS package), `DefaultProjectExtension`, `PossibleProjectExtensions`, `DisplayName`, `DisplayProjectFileExtensions`, `Language(VsTemplate)`, `Capabilities`, `PrefetchFactory`={3998970A-AEE1-4216-A54F-0A99D20A1FAB}. Without `ProjectFactoryPackage` VS says "Unsupported". The public `ProjectTypeRegistrationAttribute` (assembly `Microsoft.VisualStudio.ProjectSystem.VS`) writes exactly these.
2. **Load:** CPS throws "missing some critical imports or the referenced SDK cannot be found" (really: *no BrowseObject catalog*) unless the project contributes at least one `<PropertyPageSchema Include="x.xaml"><Context>BrowseObject</Context></PropertyPageSchema>`. A minimal `<Rule Name=... PageTemplate="generic">` with one read-only property is enough. Declare capabilities and schemas from a `.targets` imported by the project.
3. **File tree:** a `Project`-context `ProjectItemsSchema.xaml` (`ContentType`/`ItemType`/`FileExtension`), plus a `File`-context rule and a `BrowseObject`-context rule per item type (copy VS's `None.xaml` / `None.BrowseObject.xaml` from `MSBuild\Microsoft\VisualStudio\Managed\`), plus the **`UseFileGlobs`** capability for live updates of glob-included files.
4. **Configurations:** `Configurations`/`Platforms` properties, `ProjectConfiguration` items, capability `ProjectConfigurationsDeclaredAsItems`, and the `.sln` per-project mapping. The Configuration Manager and the toolbar dropdown then switch mixed C#/C++/custom projects together.
5. **Build:** plain MSBuild `Build`/`Clean`/`Rebuild` targets in the imported `.targets`; **Build > Build <Project>** runs them.
6. **F5:** set `DebuggerFlavor` = a name; define a `Project`-context rule with that name (`PageTemplate="Debugger"`) and import VS's `DebuggerGeneral.xaml` (`Context Project`); export a provider `[ExportDebugger("<name>")] [AppliesTo("<capability>")] : DebugLaunchProviderBase` returning `DebugLaunchSettings { Executable, CurrentDirectory, LaunchOperation=CreateProcess, LaunchDebugEngineGuid={3B476D35-A401-11D2-AAD4-00C04F990171}, Project=VsHierarchy }`; add capability `DisableBuiltInDebuggerServices`. `DebugLaunchOptions.NoDebug` carries Ctrl+F5.
7. **Templates:** VSIX assets `Microsoft.VisualStudio.ProjectTemplate` / `ItemTemplate` pointing at folders of loose `.vstemplate` files; `<ProjectType>` = the pkgdef's `Language(VsTemplate)`. After install: `devenv /rootSuffix Exp /updateconfiguration` and `/installvstemplates`. VS 2026's Add Item shows a compact dialog; the category list is behind "Show all templates".
8. **Compiling against CPS:** reference `Microsoft.VisualStudio.ProjectSystem*.dll` from `Common7\IDE\CommonExtensions\Microsoft\Project\` (`Private=false`) because NuGet stops at 17.9.380; they need `System.Collections.Immutable` 10.x, so add VS's copy and exclude the package's compile assets (else CS1705).
9. **Method:** `dotnet tool install --global ilspycmd`, then `ilspycmd -p -o <scratch> <dll>`, and read VS's own `.targets`/`.xaml`/`.pkgdef` before guessing. Load errors appear only via **Project > Reload Project** (dialog + Output window).

## Design lessons that held up

- **Put logic in a VS-free library** (`netstandard2.0`) and keep VS-facing code thin. It loads in both Visual Studio's MSBuild (.NET Framework) and `dotnet msbuild`, and it is unit-testable without VS. A custom MSBuild Task in that library is fine for the same reason.
- **Make the project file the source of truth** and generate everything else (language-server config, compiler command line) from the same resolved item list, so the build and the editor never disagree.
- **Model references on the language, not on C#.** For a language with no separate library compilation (Odin), a project reference means "make the source importable" (e.g. a compiler `-collection` mapping, transitive), while a reference to native C/C++ projects means link inputs. Decide the semantics per language before building the Add Reference UI.
- **Detect cycles in your own resolver** (DFS), not only in VS, so command-line MSBuild fails identically; keep VS's own dependency check as a second line.
- **Incremental builds:** if consumers recompile referenced sources, a project's inputs must include its referenced projects' sources transitively, or builds go stale.
- **Where do shipped `.targets` live?** A VSIX installs to a randomized folder, so a `.odinproj`-style project that imports `$(SomeTargetsPath)\X.targets` cannot be built from a plain shell unless you define how that path is resolved (env var, fixed per-user location, or copy). Decide early.
- **Compiler diagnostics:** prefer a structured format (e.g. JSON) over regex on text; synthesize an error code if the compiler gives none, since MSBuild's canonical format expects one. Some compilers stop after syntax errors, so a single pass may not show every error.

## Language server (LSP) notes

- **Test the server with a scripted client before writing any VS code.** A small C# console app that speaks `Content-Length`-framed JSON-RPC over the server's stdio found the real configuration needs in minutes. **Do not test from Windows PowerShell 5.1:** its process pipes made the server (ols) fail to parse headers and hang. *Verified.*
- Some servers have no `--version` and block on stdin when run bare. Identify the version from the log line or file version info, and always run them with a timeout or closeable stdin. *Verified for ols.*
- Servers that shell out to a compiler (ols runs `odin check` on `didSave`) need the compiler's absolute path in their config when it is not on PATH, and an explicit list of directories to check. A wrong check path made the server hang. Generate the server config per solution; do not hand-maintain it. *Verified.*
- Diagnostics from a compiler-backed server appear on save, not while typing, because the compiler reads files from disk. Running the checker yourself does not change that; it only duplicates the server unless you disable one side. *Reasoned; the disk-read part verified for `odin check`.*
- Classic `ILanguageClient` also needs a content type and file-extension mapping for the language; MEF discovery across a referenced assembly must be checked. *Unverified.*
- A TextMate grammar supplies highlighting through a `.pkgdef` registration (the grammar has no semantic knowledge; errors still come from the server). Recipe below. *Verified in VS 18.11 Exp.*

## TextMate grammars in VS (Verified: VS 18.11 Exp)

- **Registration (`.pkgdef`, shipped as the VsPackage asset):**
  ```
  [$RootKey$\TextMate\Repositories]
  "<Name>"="$PackageFolder$\Grammars"

  [$RootKey$\TextMate\LanguageConfiguration\GrammarMapping]
  "source.<lang>"="$PackageFolder$\Grammars\language-configuration.json"
  ```
  The folder holds `*.tmLanguage.json`; the `GrammarMapping` key is the grammar's `scopeName`. A VSIX with only these files and no assembly works.
- **The grammar needs `"fileTypes": ["ext"]`.** VS associates extensions only through it (see pitfalls). VS creates a content type `code++.<grammar name>`.
- **Grammar format:** JSON is loaded with Newtonsoft (`JsonGrammarLoader`), so comments are tolerated. Light and Dark themes color scopes differently.
- **`language-configuration.json`** is read through the same mapping. Documented properties: `comments`, `brackets`, `autoClosingPairs` (with `notIn`), `autoCloseBefore`, `surroundingPairs`, `wordPattern`, `indentationRules`, `onEnterRules`. Verified working: Toggle Line/Block Comment (the commands VS already has) and bracket highlighting. Auto-close and surround-with work once brace completion is enabled for the view (VS leaves it off for TextMate-only languages; see pitfalls: add an `IWpfTextViewCreationListener` that turns on `BraceCompletion/Enabled`, plus an `IBraceCompletionDefaultProvider` export). Not working or unproven: outlining via `folding`, `wordPattern` (completion spans come from the server; a numeric-literal `wordPattern` makes VS open a completion list inside `1.5f`, see pitfalls) (the editor's default double-click rules already select whole identifiers, so a test must use a token where the two differ, such as `1.5f`).
- **To find out how a VS feature behaves, decompile it.** `ilspycmd` (a dotnet tool; on Windows run `ilspycmd.cmd` from PowerShell) on `Common7\IDE\CommonExtensions\Microsoft\TextMate\**\Microsoft.VisualStudio.TextMate.Core.dll` and `...VSWindows.dll` into a scratch folder answered what no documentation did. Useful classes: `JsonGrammarLoader`, `LanguageConfigurationServiceLoader`, `BraceCompletionDynamicSessionProvider`, `TMUtilities`. Treat decompiled reading as a hypothesis until Exp confirms it; the TextMate `BraceCompletionDynamicSessionProvider` reading pointed at the wrong layer for auto-close, and the real cause (the `BraceCompletion/Enabled` view option was false) was found only by logging the option from a command handler.
- **Vendoring a grammar:** pin a commit, record upstream and local SHA-256 in a README next to it, ship its license in a notices file, and tag local edits (see pitfalls).

## Building a VSSDK VSIX from SDK-style projects (Verified: VS 18.11, BuildTools 18.9.820)

- A `net472` SDK-style host project plus `Microsoft.VSSDK.BuildTools` builds a `.vsix` with `MSBuild /restore`. A hand-written `source.extension.vsixmanifest` is used (MEF asset: `<Asset Type="Microsoft.VisualStudio.MefComponent" d:Source="Project" d:ProjectName="Lib" Path="|Lib|" />`), and a `ProjectReference` to a class library carries the assembly into the VSIX.
- **Set `<VSSDKBuildToolsAutoSetup>true</VSSDKBuildToolsAutoSetup>` on the host.** Without it the build succeeds but no `.vsix` is produced and nothing says why (BuildTools only imports `Microsoft.VsSDK.targets` when this is true).
- **Set `<CopyLocalLockFileAssemblies>false</CopyLocalLockFileAssemblies>`** on the host and on library projects, or ~120 Visual Studio SDK DLLs are copied to the output (they must not be shipped; VS provides them).
- The `Microsoft.VisualStudio.SDK` / `LanguageServer.Client` NuGet packages were on the 17.14 line while VS was 18.11; the BuildTools package was on 18.9. Loading them in VS 18 is checked separately.

## Environment checks (read-only)

- Visual Studio 2026 is internal version **18**; default paths use `...\Microsoft Visual Studio\18\Community`. *Verified.*
- Is the extension-development workload installed? Run `vswhere -all -prerelease -requires Microsoft.VisualStudio.Workload.VisualStudioExtension -property installationPath`, and check that `<VS>\VSSDK` exists. *Verified.*
- The VSSDK build packages (`Microsoft.VSSDK.BuildTools`) may be in `%USERPROFILE%\.nuget\packages` while `Microsoft.VisualStudio.SDK` and `Microsoft.VisualStudio.LanguageServer.Client` are not; restoring them needs network access and user approval. *Verified on one machine.*
- Only the .NET 10 SDK may be installed; VSSDK code still targets .NET Framework, so build with Visual Studio's MSBuild. *Unverified for VS 18.*

## Process

- **Spike** = a short, timeboxed experiment that answers one technical question (does X work? how hard is Y?) with throwaway code. Write down the question, the time limit, and the pass/fail criteria first; record the answer, not the code. Use spikes for the riskiest unknowns (project system, language-client discovery) before building real components.
- Keep a "milestone 0" of spikes ahead of feature milestones, and record measured effort so decisions rest on data.

### Following a project's evaluated properties from an extension (VS 18.11, *Verified* in VS Exp)

Use this to feed another component (a language server, an info bar) with what MSBuild computed, instead of parsing the project file.

- **Declare a rule.** A `.xaml` rule (context `Project`, `HasConfigurationCondition="False"`, one `StringProperty` per property) listed as a `PropertyPageSchema` makes CPS track those properties. The properties themselves come from the project's targets.
- **Subscribe by rule name.** Export `[Export(ExportContractNames.Scopes.UnconfiguredProject, typeof(IProjectDynamicLoadComponent))]` with `[AppliesTo("<capability>")]`, import `UnconfiguredProject` and `IActiveConfiguredProjectSubscriptionService`, and link an `ActionBlock<IProjectVersionedValue<IProjectSubscriptionUpdate>>` with the extension `ProjectRuleSource.SourceBlock.LinkTo(block, options, ruleNames: (IEnumerable<string>)["MyRule"])` (`DataflowExtensions`, `Microsoft.VisualStudio.ProjectSystem.dll`; `ilspycmd -t Microsoft.VisualStudio.ProjectSystem.DataflowExtensions` lists the overloads). **Without `ruleNames` the rule is simply absent** from `update.Value.CurrentState`: CPS only computes rules somebody asked for, and the keys that do arrive are the rules other components asked for (a first attempt saw `OdinCompile`, `ProjectReference` and friends but not its own rule). Reading `snapshot.Properties[...]` then gives the evaluated values, and a save of the project file arrives as a new update within a moment.
- **`[ProjectAutoLoad(startAfter, completeBy)]` goes on the `LoadAsync` method**, not on the class (CS0592). `ProjectLoadCheckpoint.ProjectFactoryCompleted` to `ProjectUpdated` worked.
- **`UnloadAsync` is not called when the project is removed from the solution.** Subscribe to `UnconfiguredProject.ProjectUnloading` (an `AsyncEventHandler`) in `LoadAsync` and release there too; the removal then arrived in the log at once.
- **Compile reference to `System.Threading.Tasks.Dataflow`:** CPS 18.x is built against 10.0, the VS SDK 17.14 packages bring 9.0 (CS1705). Same fix as for `System.Collections.Immutable`: reference VS's copy (`...\VBCSharp\LanguageServices\InteractiveHost\Desktop\System.Threading.Tasks.Dataflow.dll`, `Private=false`) and exclude the package's compile and runtime assets.
- **Keep the callback from throwing**: an exception in the `ActionBlock` delegate faults the block and ends the subscription; log and return.
- **A project-system change in the SDK needs a fresh NuGet copy** when the SDK is a same-version local package: the old copy in `~\.nuget\packages` is used until deleted, and the rule silently does not exist (see the SDK notes in the project's own docs).
