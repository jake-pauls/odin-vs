# odin-vs
Everything below amends or extends the brief. Where they conflict, this file wins. Items marked **(unverified)** come from background knowledge, not from a source or an experiment; confirm them in Milestone 0 before building on them.

## Status
- Planning and spikes only; no production code. Git repo initialized (`main`, remote `git@github.com:jake-pauls/odin-vs.git`); `.gitignore` covers build outputs. Spike code lives in the session scratchpad, not the repo.
- Machine (checked): VS 18 Community, .NET SDK 10.0.403 only. Not on PATH, installed at `C:\Program Files\Odin\`:
  - `odin\dist\odin.exe` (release zip name says `dev-2026-10`; run `odin version` in M0 to confirm)
  - `ols\ols-x86_64-pc-windows-msvc.exe` (+ `odinfmt-x86_64-pc-windows-msvc.exe`, `ols\builtin\*.odin`). Bundle copy is renamed `ols.exe`.
- **Hard requirements (user, confirmed):** `.sln`/`.odinproj`, mixed in one solution with C#/C++ projects; Configuration Manager (Debug/Release/...); Solution Explorer `Add Project`, `Add Existing Project`, `Add Item`, `Add Reference`; `.odinproj` XML is the source of truth (no property pages); all builds are native `odin` commands; cycle detection for references; designed for 10-20 projects typically, **100+ without pain**. Open Folder is rejected.
- **Project system: CPS-first spike, MPFProj as fallback** (see Project system). Blocks M4+.

## Findings on the brief

### Which of its concerns still matter (personal use, side-loaded)
| Risk | Verdict |
|---|---|
| 1 Project system cost | **Critical.** Decides whether M4-M6 are days or weeks. Spike first. |
| 2 ols vs VS LSP client | **High.** Test at protocol level before any VS code (M0). |
| 3 Version pinning | **High.** Odin ships rolling `dev-YYYY-MM` builds; ols must match. Add a startup check that compares `odin version` with the pinned ols and warns. |
| 4 ols config | **High.** Likely needs `ols.json` (collections, checker args). Generate it; do not hand-maintain. |
| 5 Cross-assembly discovery | **High, cheap to test.** Now about MEF exports (`ILanguageClient`, content type) since the new model was dropped. M0 spike. |
| 6 Grammar compatibility/license | Medium. License only matters if you redistribute; compatibility matters now. |
| 7 Semantic tokens | Low. Stay grammar-only. |
| 8 Breakpoints in `.odin` | **High.** Gate for M5; test with a hand-built exe + PDB in M0. |

### Corrections and gaps
1. **"Bundled, no path setting" is only half true.** ols locates Odin's `core`/`base`/`vendor` via the `odin` executable **(unverified)**, so Odin must still be installed. Bundling removes the ols path only. Decide how the extension finds `odin.exe` (PATH, then a setting) and report clearly when it is missing.
2. **Hybrid means the new-model code runs in-proc.** The out-of-proc crash isolation that motivates the new model is lost. Isolation of ols itself is unaffected (separate process), so the real loss is small.
3. **Install target.** Personal use on VS 2026 only: `[18.0,19.0)`. (Decision: VSSDK-only, see finding 11; the 17.14 Extensibility packages are no longer needed.)
4. **SDK/target frameworks.** VSIX/VSSDK code targets `net472` (VS 2026 still hosts VSSDK on .NET Framework **(unverified)**); installed SDK is 10, so building uses VS MSBuild with the .NET Framework targeting pack. Shared logic targets `netstandard2.0` so it loads in both VS MSBuild (net472) and `dotnet msbuild`.
5. **`OdinVS.Build` ships `.props`/`.targets` plus one task assembly.** A custom MSBuild `Task` is fine if it lives in `OdinVS.Core` (`netstandard2.0`): it makes argument building, reference resolution and cycle detection unit-testable and identical from VS and the command line. Watch for MSBuild node reuse locking the task DLL during development (`/nodeReuse:false`).
6. **`$(OdinTargetsPath)` has no defined source.** The VSIX install directory is randomized, so `msbuild Hello.odinproj` from a plain shell (M3) cannot find it. Options: environment variable, a fixed per-user location the extension writes to, or copy the targets next to the sample for M3. Decide in M0.
7. **`ProjectReference` ordering alone does not map to Odin.** Odin compiles a package plus every package it imports, from source, in one invocation. See "Reference design" below, which resolves the brief's open question.
8. **Odin error format is not MSBuild canonical.** Default position style is `file(line:col)` (`-error-pos-style:odin`); MSBuild wants `file(line,col): error CODE: msg`. Prefer `-json-errors` (stderr) parsed by a Core class, and re-emit via the task's logger; a regex over text output is the fallback. Verify the JSON schema against real failing builds in M0.
9. **Odin's link step needs MSVC environment.** Builds from a plain shell need a Developer Prompt. Inside VS this is fine; for M3 use a Developer shell.
10. **Open Folder is the cheap alternative the brief excludes.** `launch.vs.json` + `tasks.vs.json` + the LSP could deliver build, F5 and breakpoints without any project system. Confirm the `.sln`/`.odinproj` requirement is real (see Open decisions).
11. **DECIDED: VSSDK-only.** Since hybrid forfeits the new model's crash isolation, use classic `ILanguageClient` (MEF-exported, with a content-type definition for `.odin`). No `VisualStudio.Extensibility` packages, no `[VisualStudioContribution]`, so finding 5 of the brief's risk list (cross-assembly discovery) is moot; replace it with "MEF discovery of `ILanguageClient`/content type from referenced assemblies" in M0. Revisit only if `ILanguageClient` proves unusable with ols.

### Is one VSIX for LSP + VS integration sound?
Yes, for personal use. One install, shared code, one version to pin. It becomes a problem only if: (a) the new-model hosting conflicts with the VSSDK package in the same VSIX, (b) a project-system bug takes VS down (it will, it is in-proc either way), or (c) the LSP part needs a different VS target range. Mitigation: keep the LSP provider dependent only on small interfaces (`IOdinLocator`, `IServerLauncher`) so it can move into its own VSIX without rewrites.

## Project system
Cost, not capability, is the worry with CPS. Re-evaluated against the confirmed requirements:

| Requirement | MPFProj | CPS |
|---|---|---|
| 100+ projects | Weak: synchronous, UI-thread-heavy, no async load | **Strong**: built for scale (async, off-UI-thread evaluation) |
| Configuration Manager | Hand-implement configuration provider | **Strong**: configurations come from MSBuild `Configurations`/`Platforms` **(unverified)** |
| Add Project / Add Existing Project | Same for both: needs project type GUID + factory + templates | Same |
| Add Item | Hand-written; item templates | Item providers + templates; moderate |
| Add Reference (Reference Manager) | Hand-written | Capability-driven; likely the hardest piece in both |
| Debug launch (F5) | Implement debuggable-config interfaces | Launch providers; likely cleaner |
| Learning cost / docs | Source to read (ZigVS); "many engineer-months" per the (2015) MS blog | Steep, MEF, thin docs; blog suggests roughly a third of MPFProj's code |
| Failure mode | Slow and brittle at scale | Opaque MEF composition errors |

Conclusion: the 100-project and Configuration Manager requirements favor CPS; MPFProj's advantage is a readable example. Run a timeboxed **CPS spike first** (minimal `.odinproj` that loads, lists files, shows Debug/Release in Configuration Manager, builds via targets, supports Add Item, launches F5 stub). Fall back to MPFProj only if the spike stalls. Sources read (WebFetch, summarized):
- The linked MS blog is the **VS 2015-era CPS preview**, ten years old. It says a shippable MPFProj project system takes "many engineer-months" (not weeks), ~40K lines of starting code vs ~15K for a CPS type. It supports defining new project types, not only flavoring. Treat as directional only.
- `microsoft/VSProjectSystem` docs (`doc/Index.md`) map onto our needs; read in this order during the spike: `overview/define_a_new_project_type.md`, `overview/reference_implementations.md`, `overview/about_project_capabilities.md`, `overview/project_configurations.md`, `overview/threading_rules.md`, `extensibility/project_item_templates.md`, `extensibility/custom_reference_types.md`, `extensibility/reference_manager.md`, `extensibility/IValidProjectReferenceChecker.md` (hook for rejecting cyclic or invalid references), `extensibility/IDebugLaunchProvider.md`, `extensibility/IBuildUpToDateCheckProvider.md` (fast up-to-date check at 100 projects), `overview/enable_logging.md`.
- The repo advises prototyping with CPS and falling back to MPFProj for scenarios CPS doesn't support yet; confirm with a spike that these docs still match VS 2026 (the repo has breaking-change pages only up to VS 2019).

## Reference design (resolves brief's open question)
Odin has no separate compilation of libraries: building a project compiles its package directory and all imported packages from source. So an Odin-to-Odin `ProjectReference` means "make this project's source importable", not "link this artifact".

- **Source reference (default for Odin to Odin):** the referenced project exposes a collection, `-collection:<Name>=<path>`, where `Name` defaults to the project name and `OdinCollectionName`/`OdinCollectionRoot` can override. Consumers `import "name:pkg"`. Collections are **transitive**: the consumer needs the closure.
- **Link reference (Odin to native, e.g. a `.vcxproj`):** the referenced project's output (`.lib`/`.dll`/import lib) is added to the link line (`-extra-linker-flags`) **(unverified exact flags)** and ordering is enforced. This is what makes mixed C/C++ solutions work.
- **Metadata:** `ReferenceKind="Source|Link"` on `ProjectReference`, inferred from the referenced project type when absent.
- **One resolver, two consumers:** a Core task computes the reference closure and emits `OdinCollection`/`OdinLinkInput` items; the same items drive the `odin build` command line and the generated `ols.json`.
- **Cycle detection:** authoritative check in the Core resolver (DFS/Tarjan; error names the cycle path, e.g. `A -> B -> C -> A`), so it fails identically in VS and command-line MSBuild. VS's own solution dependency check is a second line of defense; also reject duplicate collection names.
- **Incrementality at scale:** because every consumer recompiles referenced sources, a project's incremental `Inputs` must include transitive referenced `OdinCompile` files, or stale builds result. Odin is fast and whole-program, so also measure a 100-project synthetic solution in M6 (generate it; do not hand-write).
- **Odin native command shape** (flags confirmed in `odin build -help`): `odin build <dir> -out:<path> [-debug] [-pdb-name:<f>] [-o:<level>] [-build-mode:exe|dll|lib|obj] [-subsystem:console|windows] -collection:A=... -define:N=V [-extra-linker-flags:"..."] -json-errors <OdinExtraArgs>`.

## Verified toolchain facts (this machine)
- `odin version` -> `dev-2026-10-nightly:84bc3fc`; `odin root` -> `C:\Program Files\Odin\odin\dist\`.
- ols logs `Starting Odin Language Server dev-2026-10`, speaks LSP over stdio with `Content-Length` framing, has **no `--version` flag**, and **blocks reading stdin**. Version checks must come from that log line or the file's product version, never from running it bare. Always give it stdin that closes, or kill it on a timeout.
- Flags that change the design:
  - `-json-errors`: structured diagnostics (see finding 8).
  - `-export-dependencies:json` + `-export-dependencies-file:<f>`: the compiler lists every source file it used, so incremental-build inputs can come from the previous build rather than from globs (also covers sources reached through collections).
  - `-show-import-graph`: DOT import graph, useful for debugging reference closures.
  - `-debug` implies `-o:none`; `-pdb-name` controls the PDB name; `-build-mode` supports `lib`/`static`, `dll`, `obj`, `test`; `-collection:name=path` is available for `import "name:pkg"` and `foreign import`.
  - `-error-pos-style:unix` exists but is also not MSBuild canonical.
- **Captured with the samples** (`samples/HelloOdin/src/main.odin` imports `hellolib:greeter` from `samples/HelloLib/greeter/lib.odin`; build with `-collection:hellolib=<samples>\HelloLib`):
  - Good build with `-debug` produces `hello.exe` plus `hello.pdb` in the `-out` directory and runs correctly. `-collection` and `-export-dependencies` work together.
  - `-json-errors` output: one JSON object on **stderr**, exit code 1. Shape: `error_count`, `errors[]`, each with `type` (`"error"`), `pos {file, offset, line, column, end_column}`, and `msgs[]` (array of strings; a first line plus possible detail lines). Paths are absolute with forward slashes. **No error code field**, so MSBuild's `CODE` must be synthesized (e.g. `ODIN0001`). Warnings' `type` value was not observed.
  - Syntax errors stop compilation before semantic checking: a file with a syntax error reports only syntax errors (3 here), and semantic errors (type mismatch, undeclared name, `'x' is not declared by 'pkg'`) appeared only after the syntax error was fixed. The parser must therefore not assume it sees every error in one pass.
  - Column can be 0 at EOF (`line 11, column 0`); clamp to 1 when forwarding.
  - `-export-dependencies:json` writes `{ "source_files": [...], "load_files": [...] }`. It listed **205 files** including Odin's own `core`/`base`/`vendor`; filter to files outside the Odin root to build the project's incremental inputs. Paths are absolute with forward slashes.
- **ols behavior (observed with a C# stdio client; PowerShell 5.1 pipes broke ols's header parsing, so do not test ols from PowerShell):**
  - Initialize advertises `textDocumentSync` (openClose, incremental, save with text), definition, typeDefinition, completion (triggers `. > # " / :`), signatureHelp, and more.
  - **Without `ols.json`, cross-collection hover/completion return nothing.** With `collections` set to `{name: "hellolib", path: <HelloLib root>}`, hover on `greeter.message` returns `greeter.message :: proc() -> string` and completion returns `message`. So `ols.json` generation from references (Reference design) is required, not optional.
  - **Diagnostics come only from `odin check`, run by ols on `textDocument/didSave`, not on didOpen/didChange.** ols logged `failed to start process for 'odin check': Not_Exist` because `odin` was not on PATH. Fix: `odin_command` in `ols.json` (absolute path to `odin.exe`) **and** `checker_path` (list of package directories to check; the default is the workspace root, which in our layout (`<project>\src\*.odin`) has no `.odin` files and made ols hang until killed).
  - With both set, three diagnostics came back after didSave with the same messages as `odin build` (type conversion, undeclared member, undeclared name), severity 1 (error), 1-based lines in our output after +1.
  - Implications: (a) the extension must generate `ols.json` per solution/workspace with `odin_command`, `checker_path` and `collections`; (b) VS's language client must send didSave (it advertises `save.includeText`); verify in M2; (c) squiggles appear on save, not as you type, which is a UX limitation to accept or work around; (d) an unresponsive ols (bad `checker_path`) must be timed out and reported by the extension; (e) ols returns syntax and semantic errors together here, unlike `odin build`.
  - Shutdown/exit works cleanly (exit code 0) when sent.
  - Tool locations: `odin_command` and `checker_path` names confirmed by behavior; other `ols.json` keys (e.g. `enable_hover`, `enable_snippets`) were accepted but their effect was not verified.
- Only the first 400 lines of `odin build -help` were read; re-read the tail (vet flags, warnings-as-errors, etc.) before finalizing option mapping. `odin check` and `odin run` exist as sibling commands but their help was not read.

## Diagnostics design note: who runs `odin check`
LSP servers normally own diagnostics and push them (`textDocument/publishDiagnostics`); ols has no analyzer of its own here, it wraps `odin check` on save, so the compiler stays the single source of truth. Start with ols as is. A stretch goal is having the extension run `odin check -json-errors` itself and feed VS error tags and the Error List (the JSON already carries line, column and end column), but it does not fix the main limit: `odin check` reads files from disk, so unsaved edits are invisible to ols and to our own runner alike unless we check a temp copy of the buffer. Our own runner would also duplicate ols's diagnostics unless ols's checker is disabled or filtered (unverified how), and needs debouncing and cancellation at 100 projects. Cheaper first step: send ols a `didSave` after an idle pause, or use VS auto-save. Revisit only if on-save latency hurts in real use.

## Open decisions
- **`OdinTargetsPath` resolution** (finding 6).
- **CPS vs MPFProj**: decided by the spike result.
- **Collection naming and `import` convention** (collection root = project directory or its `OdinPackageDir`?).

## Conventions

### C#
- `Nullable` enabled, warnings as errors, latest `LangVersion`.
- File-scoped namespaces.
- Explicit types over `var`.
- All async APIs end in `Async` and take a `CancellationToken` last.
- Parameters take the most permissive type that works (`IEnumerable<T>` over `List<T>`, `ICollection<T>` for a counted set).
- Use collection expressions (`[]`) wherever they are interchangeable with `new List<T>(...)` and similar.
- No `async void`. If an event handler forces one, wrap the whole body in `try`/`catch`.
- `internal` for anything not part of a public API. Exception: types VS requires to be public (packages, MEF/COM-visible types); note why in a one-line comment.
- Primary constructors where an explicit constructor is not needed.
- Never use `#region`.
- Match the density and style of surrounding code; do not add comments that restate the code.

### Structure
- Composition over inheritance. Types are `sealed` by default.
- Dependencies come in through the constructor as interfaces.
- Inheritance only where a VS/MPFProj base class forces it. Keep those subclasses thin, at the edge, delegating real work to composed collaborators.
- Business logic lives in `OdinVS.Core` (no `Microsoft.VisualStudio.*` references) so it is unit-testable; VS-facing projects only adapt it.

### Process (from user-level rules)
Ask before dependency add/upgrade, network access beyond registries, file deletion, or running anything not defined by project commands. Commits and merges follow "Git workflow" below. Push only to `git@github.com:jake-pauls/`.

### Git workflow
Rules 1-6 are the user's; 7-11 were proposed by Claude and confirmed by the user.
1. Work for each milestone lives on a **milestone branch** until the milestone is complete: `milestone/m<N>-<slug>`, e.g. `milestone/m0-spikes`. (A branch named `m0` could not coexist with task branches `m0/...`, hence the `milestone/` prefix.)
2. Work on a milestone's tasks happens on **task branches prefixed with the milestone**, cut from the milestone branch: `m<N>/<task-slug>`, e.g. `m0/cps-load-check`.
3. A task branch merges **up** into its milestone branch only when the task is complete.
4. When a milestone is complete, its milestone branch merges into `main`.
5. **Always ask for the user's code review before merging a branch up** (task into milestone, milestone into main). Merging **down** (milestone into a task branch, to keep it current) needs no approval.
6. Commit as you work, at the end of each finished segment of a task; the user reviews the whole task branch before it merges up, like a pull request. Commits on task branches are pre-approved; commits directly on a milestone branch or `main` are not (except changes the user directs).
7. Merge up with `--no-ff` so task history stays visible. Never force-push, reset, rebase published branches, or delete branches without asking.
8. Before asking for review, say what changed, which checklist items it satisfies, and what was run (tests, builds); push the task branch for review only when the user agrees.
9. Commit messages end with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`. The repo-local identity is `jake-pauls <jakepauls99@gmail.com>`.
10. Pushing: verify `git remote get-url origin` matches `git@github.com:jake-pauls/` first. Git Bash cannot see the Windows ssh-agent, so push from PowerShell with `$env:GIT_SSH_COMMAND = 'C:/Windows/System32/OpenSSH/ssh.exe'`.
11. **Decided:** all spike code goes under `spikes/<name>/` on its task branch, never into `src/` or `tests/` (the scratchpad is session-local and unreviewable); promote real pieces (e.g. `OlsClient` into `OdinVS.Core.Tests`) in later tasks.

## Architecture additions
```
src/OdinVS.Core/            # netstandard2.0, no VS refs: error-output parser, odin arg builder,
                            # ols.json generator, version-compat check, .odinproj reader
tests/OdinVS.Core.Tests/    # MSTest
tests/OdinVS.Build.Tests/   # MSTest: runs msbuild on samples with a fake odin.exe
tests/fake-odin/            # stub that emits canned Odin output/exit codes
```
`OdinVS.LanguageServer` and `OdinVS.ProjectSystem` call into `Core`. If M0 shows the MEF `ILanguageClient`/content-type exports are not discovered from a referenced assembly, fold `LanguageServer` into the host project.

## Testing strategy
Tests are chosen by what can fail, cheapest first.

| Tier | What | How | Automated |
|---|---|---|---|
| 1 Unit | Odin error parser, `odin build` argument builder, `ols.json` generator, version-compat check, `.odinproj` property mapping | MSTest on `OdinVS.Core`; table-driven cases using captured real Odin output as fixtures | Yes |
| 2 ols protocol | ols works with a generic LSP client: `initialize`, `didOpen` of a broken file, expect diagnostics | Small test that spawns the pinned `ols.exe`, speaks JSON-RPC over stdio. Runs before any VS code and re-runs when ols is bumped | Yes (needs ols + Odin installed; mark `[TestCategory("RequiresToolchain")]`) |
| 3 MSBuild | `Odin.targets`: build, clean, rebuild, incremental skip, Debug emits PDB, error mapping, missing odin | `msbuild` against `samples/` with `tests/fake-odin` (fast, deterministic) plus a small real-Odin subset | Yes |
| 4 VSIX smoke | Colorization, squiggles, Error List, project load, F5, breakpoints | Manual checklists below in the Experimental instance | No, until proven worth it |
| 5 VS integration | Scripted UI tests in a VS instance | Defer. The VS integration-test library may be xUnit-oriented **(unverified)**; revisit after M4 | Later |

Rules: every Core change ships with a tier-1 test; every `Odin.targets` change ships with a tier-3 test; the VSIX smoke checklist is run, and results noted, before declaring a milestone done. Grammar changes get a fixture `.odin` file in `samples/` that exercises each token category, reviewed visually; automated grammar tests would need a new dependency (ask first).

Build/test commands (record real ones here as they are established):
- Build VSIX: VS MSBuild with `/restore` (see skill). Unit tests: `dotnet test tests/OdinVS.Core.Tests`.
- Experimental instance: F5 from the host project. Verify installs in the instance, not by exit code.
- Uninstall via Manage Extensions, not `VSIXInstaller /u`.

## Milestone checklists
Each item must be demonstrably true; "done" means checked off with a note of how it was verified.

### M0 Spikes (new; before any production code)
- [x] Odin and ols installed under `C:\Program Files\Odin\`; version/help/root recorded in "Verified toolchain facts". `-json-errors` and `-export-dependencies:json` captured (see facts). Remaining: read the rest of `odin build -help`, `odin check -help`, observe a warning in JSON, and save trimmed real outputs as test fixtures under `tests/OdinVS.Core.Tests/Fixtures/`.
- [x] Tier-2 protocol check passes with a throwaway C# client (scratchpad `OlsClient`, to be moved into the Core test project): initialize, hover, completion and diagnostics all work against ols once configured. Details in "ols behavior" below. **PowerShell attempts failed (kept for the record):** a throwaway PowerShell client (scratchpad `ols-smoke.ps1`, not in the repo) got `Starting Odin Language Server dev-2026-10` then `Failed to read and parse header` right after `initialize`, and the script then hung (a stray ols/shell process may remain). Second attempt gave the same result (and left a hung process again). The BOM theory is now unlikely: the script writes through `StandardInput.BaseStream`, which bypasses the writer's encoding, and `ProcessStartInfo.StandardInputEncoding` does not exist in Windows PowerShell 5.1. Remaining hypotheses: (2) ols wants a different header form or reads in a way PowerShell's pipe handling breaks; (3) the unread stderr pipe blocks. **Stop iterating in PowerShell.** Build the client as a throwaway C# console app (no NuGet packages needed) to settle it, then move it into the Core test project.
- [x] Throwaway VSSDK VSIX builds with VS MSBuild and loads in Exp (done; see the loaded/MEF items below). Packages restored (nuget.org, user-approved) into the scratchpad spike `VsixSpike`: `Microsoft.VisualStudio.SDK` 17.14.40265, `Microsoft.VisualStudio.LanguageServer.Client` 17.14.60, `Microsoft.VSSDK.BuildTools` 18.9.820 (125 packages total, net472). The SDK/Client packages are the 17.14 line while VS is 18.11; confirm they load in VS 18 during this spike. **Build half done (scratchpad `VsixSpike` + `OdinSpike.Lsp`):** an SDK-style `net472` host project plus an `ILanguageClient`/content-type class library builds with VS MSBuild (`/restore /t:Rebuild`) into `VsixSpike.vsix`, which contains `OdinSpike.Lsp.dll` and a generated manifest with the MEF asset path resolved, so referenced-assembly packaging works. Needed settings: `VSSDKBuildToolsAutoSetup=true` on the host (otherwise no `.vsix` is produced, silently) and `CopyLocalLockFileAssemblies=false` on both projects (otherwise ~120 VS SDK DLLs are copied to the output). Still to do: install into the Exp hive, open a `.odin` file, confirm MEF discovery and `ActivateAsync` via `%TEMP%\odinspike.log`, and check the Exp `ComponentModelCache` error log.
- [x] VSIX spike installed and loaded in VS Exp (18.11) with the 17.14-line SDK/Client packages: no `ComponentModelCache` `.err` file. **Caveat:** I passed `/admin` to `VSIXInstaller` (not needed, my mistake), which installed the spike per-machine under `...\IDE\Extensions\hcvrc1z3.w0e\`, so it is also active in the main VS instance; uninstall it via Manage Extensions. Use `/rootSuffix:Exp` without `/admin` next time.
- [x] MEF discovery of `ILanguageClient` + content type from a referenced assembly: **works**. Opening `main.odin` in Exp logged `OnLoadedAsync`, `ActivateAsync`, `ols started`, `OnServerInitializedAsync` in `%TEMP%\odinspike.log`, so VS's client launched ols and completed `initialize`. Not yet checked: whether VS sends `didSave`, hover/completion, and diagnostics (needs `ols.json` with `odin_command`/`checker_path`); three `ols` processes were seen running afterwards (one is the spike's pid 19448, the others unexplained, possibly earlier test leftovers), so check ols is stopped on VS exit in M2.
- [ ] CPS spike (see Project system), done in two phases so impossibilities surface cheaply. **Timebox:** no fixed cap; the user will run this in short stretches (a few hours at first) and may say stop at any time. **When told to stop, stop immediately and report progress so far** (which items are done, evidence, what is in flight, and the next step). Independently, stop and reassess as soon as any Phase A check returns "impossible". Record elapsed effort and the outcome of each item so the CPS-vs-MPFProj decision rests on measured effort.
  - **Phase A: feasibility checks (reading and tiny probes; no project system built yet).** Each ends "feasible", "feasible with caveat: ...", or "impossible: ..." plus the evidence. Reading online docs needs network approval.
    - [x] A0 (feasible with caveat; spikes/cps-feasibility/FINDINGS.md) Authoring surface: CPS authoring packages exist for VS 18 (ask before any NuGet search), the `VSProjectSystem` docs still match, and at least one reference implementation of a custom CPS project type can be found to copy from. *Impossible if:* no public packages/APIs for VS 18, or the docs describe only internals.
    - [x] A1 (feasible; VS ships a pkgdef-registered CPS type to copy) Load: how a custom project type is registered (project type GUID, factory/capabilities, `.pkgdef`) and that a bare `.odinproj` can be opened as a CPS project from a `.sln`/`.slnx`. *Impossible if:* new project types cannot be defined (only flavored).
    - [x] A2 (feasible with caveat; globbing untested) File tree: custom item types (`OdinCompile`) and the project tree provider can show `.odin` files from MSBuild items under the project node. *Impossible if:* custom item types cannot appear in the tree.
    - [x] A3 (likely feasible, unproven; B3 is decisive) Configurations: the project participates in the **solution configuration** (Debug/Release x64) so the Configuration Manager and the top-bar configuration dropdown switch the Odin project together with C# and C++ projects in the same solution. A fixed set of configurations (Debug, Release) is acceptable; user-defined ones are a bonus. *Impossible if:* an Odin project cannot be mapped into the solution configuration, so the Configuration Manager or the toolbar dropdown cannot switch it together with the other projects.
    - [x] A4 (feasible; pkgdef registers template dirs) Add Item and Add Project: item templates (`.vstemplate`) work for a custom project type through the standard Add > New Item dialog, and project templates appear in the standard New Project / Add > New Project dialog (Add Existing Project needs only the registered project type). *Impossible if:* Add Item or project templates cannot be supplied for a custom type.
    - [x] A5 (feasible with caveat; exe/args/Ctrl+F5 untested) F5 stub: a debug launch provider can run an `.exe` (`$(TargetPath)`) under the native debug engine with working directory and arguments. *Impossible if:* custom types cannot supply a launch provider.
  - **Phase B: walking skeleton (only for sub-goals A said are feasible).** Pass criteria are what a person sees in VS Exp.
    - [x] B1 Load (passed 2026-10-30): `spikes/cps-feasibility/sample/HelloOdin/HelloOdin.odinproj` opens in `CpsSpike.sln` in VS Exp 18.11 under our own project type GUID, and the toolbar shows Debug and x64 dropdowns. Needed: `ProjectFactoryPackage` in the pkgdef plus a BrowseObject rule schema (see FINDINGS.md).
    - [x] B2 File tree (passed 2026-10-30): HelloOdin > src > main.odin, util.odin appear, and a file added on disk while VS was open (live.odin) appeared within ~10 s. Needed: item rules (`OdinCompile.xaml` File context + `OdinCompile.BrowseObject.xaml`) and the `UseFileGlobs` capability.
    - [x] B3 Configurations (passed 2026-10-30; evidence in FINDINGS.md): in a solution containing the Odin project plus a C# and a C++ project, Debug and Release x64 appear in the Configuration Manager and the top-bar dropdown; switching either one switches all three projects, and the Odin project's stub target echoes the matching MSBuild property.
    - [ ] B4 Add Item: Add > New Item opens VS's standard dialog, an "Odin File" template is listed, and choosing it creates an `.odin` file that appears in the tree and the `.odinproj`.
    - [ ] B4-2 Add Project: File > New > Project and Add > New Project (the standard creation window) list an "Odin Executable" template. The window accepts a project name and location and generates a folder containing `<Name>.odinproj` and `src/main.odin` that opens, builds as far as the stub target allows, and is added to the solution. *Stretch, later:* more templates (dynamic library, static library).
    - [x] B5 F5 stub (passed 2026-10-30): F5 (toolbar Start) launched the exe built by the project's Build target under the native debugger; Output > Debug showed HelloOdin.exe loading and exiting with code 0. Needed: `DebuggerFlavor`, VS's `DebuggerGeneral.xaml`, our `OdinDebugger.xaml`, and a small `[ExportDebugger("OdinDebugger")]` provider (see FINDINGS.md). Breakpoint binding is still the separate M0 item below.
  - [ ] C Decision recorded: CPS go / no-go (and which sub-goals, if any, need MPFProj) with a short rationale and effort table, added to "Project system".
- [ ] A hand-built exe + PDB from `odin build -debug` hits a breakpoint in an `.odin` file when opened via Debug > Attach/Open as project (gate for M5).
- [ ] `OdinTargetsPath` strategy chosen (finding 6).
- [ ] Project system decision made and a one-page rationale added to this file.

### M1 Colorization
- [ ] Grammar chosen, license noted, checked in under `Grammars/`.
- [ ] `odin.pkgdef` registers it; fixture `.odin` shows keywords, strings, numbers, comments, procs, directives, attributes in Exp.
- [ ] Bracket matching/comment toggling work if `language-configuration.json` is supplied.

### M2 Language server
- [ ] ols launches from the VSIX, stdio piped, stopped cleanly on VS exit.
- [ ] Broken fixtures produce squiggles and Error List entries; fixing them clears both.
- [ ] Completion, go-to-definition, hover checked and results recorded (including which do not work).
- [ ] Missing `odin`/`ols` and ols crash paths log and set `Enabled = false` without hanging VS.
- [ ] Version-compat warning verified with a deliberately mismatched ols.

### M3 Command-line build
- [ ] `Odin.props/targets` build `samples/HelloOdin` from a Developer shell.
- [ ] Tier-3 tests cover: Debug PDB, Release, Clean, Rebuild, incremental skip, error mapping, missing odin.
- [ ] Compile errors surface as canonical MSBuild errors (checked by tier-3 test).

### M4 Project in solution (blocked on project-system decision)
- [ ] `.odinproj` opens in a `.sln`/`.slnx` without errors; files appear in Solution Explorer.
- [ ] Build/Clean/Rebuild from the menu; errors reach the Error List; double-click navigates.
- [ ] Configuration Manager shows Debug/Release x64 and switching changes output.
- [ ] Template creates a working project via File > New.

### M5 Debugging
- [ ] F5 launches `$(TargetPath)` with project working dir and args.
- [ ] Breakpoints in `.odin` bind and hit; Locals/Watch usable for ints, strings, slices (note gaps).
- [ ] Ctrl+F5 runs without debugger.

### M6 Multi-project
- [ ] Source and Link references implemented per Reference design; tier-1 tests for closure, duplicates, cycles.
- [ ] Add Reference dialog creates working references; circular reference is rejected with the cycle path.
- [ ] Odin + C++ (`.vcxproj`) + C# mixed sample builds and F5 runs.
- [ ] Library source change rebuilds dependents; unchanged graph skips.
- [ ] ols receives collections generated from references.
- [ ] Generated 100-project solution loads and builds; load time and build time recorded.

## Maintenance notes
- Record pinned versions (Odin, ols, VS, Extensibility packages) in one table here once chosen; update on every bump.
- Add verified pitfalls discovered along the way to a `docs/pitfalls.md` rather than growing this file.
