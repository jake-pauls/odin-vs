# odin-vs
Everything below amends or extends the brief. Where they conflict, this file wins. Items marked **(unverified)** come from background knowledge, not from a source or an experiment; confirm them in Milestone 0 before building on them.

## Status
- Planning and spikes only; no production code. Git repo initialized (`main`, remote `git@github.com:jake-pauls/odin-vs.git`); `.gitignore` covers build outputs. Spike code lives under `spikes/<name>/`. Longer material lives in `docs/`: `milestones.md` (checklists, M0 cost), `toolchain.md` (verified Odin/ols facts), plus the original brief and its after-exploration snapshot.
- Machine (checked): VS 18 Community, .NET SDK 10.0.403 only. Not on PATH, installed at `C:\Program Files\Odin\`:
  - `odin\dist\odin.exe` (release zip name says `dev-2026-10`; run `odin version` in M0 to confirm)
  - `ols\ols-x86_64-pc-windows-msvc.exe` (+ `odinfmt-x86_64-pc-windows-msvc.exe`, `ols\builtin\*.odin`). Bundle copy is renamed `ols.exe`.
- **Hard requirements (user, confirmed):** `.sln`/`.odinproj`, mixed in one solution with C#/C++ projects; Configuration Manager (Debug/Release/...); Solution Explorer `Add Project`, `Add Existing Project`, `Add Item`, `Add Reference`; `.odinproj` XML is the source of truth (no property pages); all builds are native `odin` commands; cycle detection for references; designed for 10-20 projects typically, **100+ without pain**. Open Folder is rejected.
- **Project system: CPS (decided 2026-10-30 after the M0 spike; see "Project system decision").** MPFProj is not planned.

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

### Project system decision (2026-10-30): CPS, go
All five spike capabilities (load, file tree, configurations, Add Item, F5) plus Add Project work with CPS in VS Exp 18.11; details and evidence are in `spikes/cps-feasibility/FINDINGS.md`. MPFProj is no longer planned.

| Capability | Result | Rough effort (my in-session estimate, not measured; probably high, see "M0 cost" in the checklist) |
|---|---|---|
| A0-A5 feasibility reading | no blockers | ~1.5 h |
| B1 Load | pass; needed `ProjectFactoryPackage` in the pkgdef + a BrowseObject rule | ~3 h (two dead ends) |
| B2 File tree | pass; needed item rules + `UseFileGlobs` | ~0.7 h |
| B3 Configurations | pass; Config Manager + toolbar switch Odin, C# and C++ together | ~0.7 h |
| B5 F5 | pass; needed `DebuggerFlavor` + rules + a small `[ExportDebugger]` provider (C#) | ~2 h |
| B4 / B4-2 templates | pass; item and project templates through VSIX assets + `Language(VsTemplate)` | ~1 h |

What this means for the plan:
- **Decompiling is the documentation.** `ilspycmd` on `Microsoft.VisualStudio.ProjectSystem*.dll` (in `Common7\IDE\CommonExtensions\Microsoft\Project\`) and reading VS's own `MSBuild\Microsoft\VisualStudio\Managed\*.xaml|targets` answered what the VS 2015-era docs could not. Budget for it in M4.
- **Build setup:** compile the project-system code against the VS-installed assemblies (`HintPath`, `Private=false`); public CPS NuGet packages stop at 17.9.380. Expect to re-check compile references when VS updates.
- **Still unproven (carry into M4-M6):** Add Reference / Reference Manager and the `IValidProjectReferenceChecker` cycle hook; Build-before-F5 ordering and reading `TargetPath` instead of the spike's hard-coded `bin\<cfg>\<name>.exe`; the real `Odin.targets` and `$(OdinTargetsPath)` (the spike's template hard-codes an absolute path); `.slnx`; user-defined solution configurations beyond Debug/Release; whether breakpoints bind in `.odin` (separate M0 gate); more project templates (dynamic/static library); behavior at 100+ projects.
- **Spike-to-product mapping:** `OdinCps.pkgdef` becomes a `ProjectTypeRegistration`-style registration; `odin-build/Odin.targets` + `Rules/*.xaml` become `OdinVS.Build`; `OdinDebugLaunchProvider` and the templates move into `OdinVS.ProjectSystem`; pure logic (arg building, reference closure) goes to `OdinVS.Core`.

## Reference design (resolves brief's open question)
Odin has no separate compilation of libraries: building a project compiles its package directory and all imported packages from source. So an Odin-to-Odin `ProjectReference` means "make this project's source importable", not "link this artifact".

- **Source reference (default for Odin to Odin):** the referenced project exposes a collection, `-collection:<Name>=<path>`, where `Name` defaults to the project name and `OdinCollectionName`/`OdinCollectionRoot` can override. Consumers `import "name:pkg"`. Collections are **transitive**: the consumer needs the closure.
- **Link reference (Odin to native, e.g. a `.vcxproj`):** the referenced project's output (`.lib`/`.dll`/import lib) is added to the link line (`-extra-linker-flags`) **(unverified exact flags)** and ordering is enforced. This is what makes mixed C/C++ solutions work.
- **Metadata:** `ReferenceKind="Source|Link"` on `ProjectReference`, inferred from the referenced project type when absent.
- **One resolver, two consumers:** a Core task computes the reference closure and emits `OdinCollection`/`OdinLinkInput` items; the same items drive the `odin build` command line and the generated `ols.json`.
- **Cycle detection:** authoritative check in the Core resolver (DFS/Tarjan; error names the cycle path, e.g. `A -> B -> C -> A`), so it fails identically in VS and command-line MSBuild. VS's own solution dependency check is a second line of defense; also reject duplicate collection names.
- **Incrementality at scale:** because every consumer recompiles referenced sources, a project's incremental `Inputs` must include transitive referenced `OdinCompile` files, or stale builds result. Odin is fast and whole-program, so also measure a 100-project synthetic solution in M6 (generate it; do not hand-write).
- **Odin native command shape** (flags confirmed in `odin build -help`): `odin build <dir> -out:<path> [-debug (Debug configuration only)] -strict-style [-pdb-name:<f>] [-o:<level>] [-build-mode:exe|dll|lib|obj] [-subsystem:console|windows] -collection:A=... -define:N=V [-extra-linker-flags:"..."] -json-errors <OdinExtraArgs>`.

## Verified toolchain facts
Moved to [docs/toolchain.md](docs/toolchain.md): odin/ols versions, `-json-errors` and `-export-dependencies` shapes, observed ols behavior (needs `ols.json` with `odin_command`, `checker_path`, `collections`; diagnostics only on `didSave`), and flags that shaped the design. Read it before touching the build targets, the error parser or the language client.

## Diagnostics design note: who runs `odin check`
LSP servers normally own diagnostics and push them (`textDocument/publishDiagnostics`); ols has no analyzer of its own here, it wraps `odin check` on save, so the compiler stays the single source of truth. Start with ols as is. A stretch goal is having the extension run `odin check -json-errors` itself and feed VS error tags and the Error List (the JSON already carries line, column and end column), but it does not fix the main limit: `odin check` reads files from disk, so unsaved edits are invisible to ols and to our own runner alike unless we check a temp copy of the buffer. Our own runner would also duplicate ols's diagnostics unless ols's checker is disabled or filtered (unverified how), and needs debouncing and cancellation at 100 projects. Cheaper first step: send ols a `didSave` after an idle pause, or use VS auto-save. Revisit only if on-save latency hurts in real use.

## Open decisions
- ~~`OdinTargetsPath` resolution~~ **Decided: MSBuild SDK `Odin.Sdk` shipped as a NuGet package** (finding 6; see `spikes/odin-targets-path/FINDINGS.md`). **Feed decided (user, 2026-11-01): the `.nupkg` is checked into `feed/` of this private repo** so it can be tested on another machine. It is marked LOCAL ONLY in its metadata (title, description, tags, `-local` version suffix, embedded README), in `feed/README.md` and by a pack-time warning, and is never published.
- **Decided (user, 2026-10-30): `.odinproj` authors own their file list.** The SDK adds no `OdinCompile` glob or other items; each project (and its template) lists its own, e.g. `<OdinCompile Include="src\**\*.odin" />`. Intent: the `.odinproj` stays the readable source of truth, with no hidden defaults about what compiles. The SDK only supplies conditional defaults that a project can override, following `Microsoft.NET.Sdk`: `Configurations` = `Debug;Release`, `Configuration` = `Debug`, `Platforms`/`Platform` = `x64` (single platform for now). See `spikes/odin-targets-path/FINDINGS.md`.
- ~~CPS vs MPFProj~~ **Decided: CPS** (see "Project system decision").
- **Where the `odin.exe` location and default build flags come from** (stretch, late milestone; PR #1 review): `Odin.targets` must not hard-code `C:\Program Files\Odin\odin\dist\odin.exe` for every user. Read it from a user-level setting (the extension's options page, written to a per-user file the targets read), then PATH. Likewise let users extend the default flags (`-strict-style`, plus `-debug` only when `$(Configuration)` is `Debug`) via `OdinExtraArgs`. Interacts with finding 6.
- **Collection naming and `import` convention** (collection root = project directory or its `OdinPackageDir`?).

## Conventions

### C#
- Every C# `.csproj` sets `<LangVersion>latest</LangVersion>`, `<Nullable>enable</Nullable>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` directly. No `Directory.Build.props` for these. (MSBuild `MSB3277` version-conflict warnings in VSIX builds are not promoted to errors.)
- File-scoped namespaces.
- Explicit types over `var`.
- All async APIs end in `Async` and take a `CancellationToken` last.
- Parameters take the most permissive type that works (`IEnumerable<T>` over `List<T>`, `ICollection<T>` for a counted set).
- Use collection expressions (`[]`) wherever they are interchangeable with `new List<T>(...)` and similar.
- No `async void`. If an event handler forces one, wrap the whole body in `try`/`catch`.
- `internal` for anything not part of a public API. Exception: types VS requires to be public (packages, MEF/COM-visible types); note why in a one-line comment.
- Primary constructors where an explicit constructor is not needed.
- Private fields are prefixed `m_`, e.g. `m_MemberName` (instance and static; `const` stays PascalCase). Captured primary-constructor parameters are not members and keep their parameter name.
- Properties are always PascalCase, e.g. `ExampleProperty`.
- Prefer `is` / `is not` for null checks, type checks and comparisons with constants (`x is null`, `name is "ols"`); `==` stays for non-constant values.
- Always use braces on `if`/`else`/loops, never single-line bodies:
  ```
  if (MyBooleanCondition)
  {
      MyLogic();
  }
  ```
- In `switch`, put each statement of a case on its own line; never `case X: Do(); break;` on one line.
- Native methods and their structs live in an `internal static class NativeMethods` (`src/OdinVS.LanguageServer/NativeMethods.cs`), not next to the code that calls them.
- Strings that assume something about the local machine (an install path, a tool location) are a named `const` or a parameter with a default, defined once and reused; never repeated inline. Say in a comment what is assumed.
- Never swallow an exception silently. At minimum log it with the exception text and, where there is one, what to do about it (what to check or change). Empty `catch` blocks are allowed only where logging is impossible or pointless, and then a comment says why: the logger itself failing, the expected end of a stream or `OperationCanceledException`, a best-effort cleanup whose failure changes nothing for the user. In `OdinVS.Core` (no logger) return the problem to the caller (e.g. `ResolvedTools.Problems`) instead of dropping it.
- Never use `#region`.
- Match the density and style of surrounding code; do not add comments that restate the code.

### Structure
- Composition over inheritance. Types are `sealed` by default.
- Dependencies come in through the constructor as interfaces.
- Inheritance only where a VS/MPFProj base class forces it. Keep those subclasses thin, at the edge, delegating real work to composed collaborators.
- Business logic lives in `OdinVS.Core` (no `Microsoft.VisualStudio.*` references) so it is unit-testable; VS-facing projects only adapt it.

### Diagnostics
The user-facing table of diagnostic codes (`ODIN0001`, `ODIN0002`, `ODIN0100`, `ODIN0101`, the `ols.*` codes) is in [docs/user-guide.md](docs/user-guide.md). When you add, change or remove any error, warning or other diagnostic, update that table in the same change, and add a tier-1 or tier-3 test for it. The `ODINnnnn` numbering is the user's accepted scheme (2026-11-01): compile errors/warnings 000x, build-process failures 01xx.

### Process (from user-level rules)
Ask before dependency add/upgrade, network access beyond registries, file deletion, or running anything not defined by project commands. Branches, commits and PRs follow "Git workflow" below. Push only to `git@github.com:jake-pauls/`.

### Git workflow
Simplified 2026-10-30 (user): there are no milestone branches. Every task is its own branch off `main`, reviewed as its own PR, so branches never stack.
```
main
| - m0/cps-feasibility
| - m0/odin-breakpoints
| - m1/<task-slug>
```
1. **One task, one branch,** named `m<N>/<task-slug>` (milestone number, then a short slug), always cut from an up-to-date `main`. Never cut a task branch from another task branch.
2. **One PR per task branch into `main`.** The user reviews and merges it; I never merge into `main` myself.
3. Commit as you work, at the end of each finished segment of a task. Commits on task branches are pre-approved; commits directly on `main` are not (except changes the user directs).
4. Before a task branch is cut, `git fetch` and fast-forward `main` (from PowerShell, see rule 9). If a task depends on an unmerged task, wait for that PR to merge instead of stacking.
5. Push a task branch and open its PR only when the user asks (or has said to, for that task). The PR description says what changed, which `docs/milestones.md` items it satisfies, and what was run (tests, builds).
6. Never force-push, reset, rebase published branches, or delete branches without asking. If `main` moves, merge `main` into the task branch.
7. Milestone checklist ticks are proposals until the user merges the PR; say so in the PR description.
8. Commit messages end with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`. The repo-local identity is `jake-pauls <jakepauls99@gmail.com>`.
9. Pushing: verify `git remote get-url origin` matches `git@github.com:jake-pauls/` first. Git Bash cannot see the Windows ssh-agent, so push from PowerShell with `$env:GIT_SSH_COMMAND = 'C:/Windows/System32/OpenSSH/ssh.exe'`.
10. **Review before the PR:** when the user says "review", "review and open a PR" or similar, follow `.claude/skills/review-branch/SKILL.md` (also `/review-branch`): agent review, fix trivial findings, ask about the rest, then open the PR with a "please check" list.
12. **Test in VS before committing anything that affects VS.** If a change touches what Visual Studio sees (the SDK props/targets and CPS rules, the project system, the VSIX, grammar or language configuration, the language client), load it in the Experimental instance and check the affected behavior there before the commit. Command-line `msbuild` and unit tests are not a substitute: M3's first commit passed all of them yet the Configuration Manager was broken in VS (see `docs/pitfalls.md`). Say in the commit or milestone note what was checked in VS, and what was not.
11. **Decided:** all spike code goes under `spikes/<name>/` on its task branch, never into `src/` or `tests/` (the scratchpad is session-local and unreviewable); promote real pieces (e.g. `OlsClient` into `OdinVS.Core.Tests`) in later tasks.

## Architecture additions
```
src/OdinVS.Core/            # netstandard2.0, no VS refs: error-output parser, odin arg builder,
                            # ols.json generator, version-compat check, .odinproj reader
tests/OdinVS.Core.Tests/    # MSTest
src/OdinVS.Build/           # Odin.Sdk package source: Sdk.props/targets, CPS rules, task DLL from Core. LOCAL ONLY, see feed/README.md
feed/                       # checked-in local feed holding Odin.Sdk.*-local.nupkg (private repo; never publish)
tests/OdinVS.Build.Tests/   # MSTest tier 3: packs the SDK to a scratch feed, runs `dotnet msbuild` on copies of tests/samples with real odin
                            # (no fake-odin: real odin is installed and fast; "missing odin" uses a bad OdinExe)
```
`OdinVS.LanguageServer` and `OdinVS.ProjectSystem` call into `Core`. If M0 shows the MEF `ILanguageClient`/content-type exports are not discovered from a referenced assembly, fold `LanguageServer` into the host project.

## Testing strategy
Tests are chosen by what can fail, cheapest first.

| Tier | What | How | Automated |
|---|---|---|---|
| 1 Unit | Odin error parser, `odin build` argument builder, `ols.json` generator, version-compat check, `.odinproj` property mapping | MSTest on `OdinVS.Core`; table-driven cases using captured real Odin output as fixtures | Yes |
| 2 ols protocol | ols works with a generic LSP client: `initialize`, `didOpen` of a broken file, expect diagnostics | Small test that spawns the pinned `ols.exe`, speaks JSON-RPC over stdio. Runs before any VS code and re-runs when ols is bumped | Yes (needs ols + Odin installed; mark `[TestCategory("RequiresToolchain")]`) |
| 3 MSBuild | `Sdk.targets`: build, clean, rebuild, incremental skip, Debug emits PDB, error mapping, missing odin | `dotnet msbuild` against copies of samples under `tests/samples/` with real odin (`RequiresToolchain`; no fake-odin) | Yes |
| 4 VSIX smoke | Colorization, squiggles, Error List, project load, F5, breakpoints | Manual checklists below in the Experimental instance | No, until proven worth it |
| 5 VS integration | Scripted UI tests in a VS instance | Defer. The VS integration-test library may be xUnit-oriented **(unverified)**; revisit after M4 | Later |

Rules: every Core change ships with a tier-1 test; every `Odin.targets` change ships with a tier-3 test; the VSIX smoke checklist is run, and results noted, before declaring a milestone done. Grammar changes get a fixture `.odin` file under `tests/samples/` that exercises each token category, reviewed visually; automated grammar tests would need a new dependency (ask first).

Build/test commands (record real ones here as they are established):
- Build VSIX: VS MSBuild with `/restore` (see skill). Unit tests: `dotnet test tests/OdinVS.Core.Tests`. Tier-3 build tests: `dotnet test tests/OdinVS.Build.Tests` (about 10 s, real odin). After changing `src/OdinVS.Build` or the task in Core, run `scripts\pack-sdk.ps1` and commit the new `feed/*.nupkg`; it is local-only and must never be pushed to any package feed (user, 2026-11-01).
- Rebuild, reinstall into Exp and launch it on a sample: `scripts\exp-reinstall.ps1 [-Bump] [-OpenFile <path>]` (closes any running Exp, `-Bump` raises the manifest patch version). Known quirks are in `docs/pitfalls.md`.
- Bundled ols is pinned: `src/OdinVS/ols/ols.pin` holds its version and SHA-256, the `VerifyBundledOls` target in `OdinVS.csproj` fails the build if `ols.exe` differs, and `BundledOlsPinTests` checks the same plus `BundledOls.Version`. Replace ols only with `scripts\bump-ols.ps1 [-Version dev-YYYY-MM]` (copies from the local Odin install, updates the pin, `BundledOls.cs` and `THIRD-PARTY-NOTICES.md`, runs the tier-2 tests).
- Experimental instance: F5 from the host project. Verify installs in the instance, not by exit code.
- Uninstall via Manage Extensions, not `VSIXInstaller /u`.

## Milestone checklists
Moved to [docs/milestones.md](docs/milestones.md) (M0 spikes through M6 multi-project, with the M0 cost line). Update it as items are verified; every item needs a note of how it was verified.

## Maintenance notes
- Record pinned versions (Odin, ols, VS, Extensibility packages) in one table here once chosen; update on every bump.
- Add verified pitfalls discovered along the way to a `docs/pitfalls.md` rather than growing this file.
