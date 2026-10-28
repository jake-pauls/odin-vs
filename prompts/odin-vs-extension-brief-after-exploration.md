# Odin for Visual Studio 2026: brief after exploration

> **Frozen snapshot, 2026-10-28.** Not maintained. It predates the ols behavior findings (`ols.json`, `odin_command`, `checker_path`, diagnostics on save), the diagnostics design note, and the pinned VSSDK packages. `CLAUDE.md` is authoritative; use this file only to see how the design moved away from the original brief.

Condensed record of what changed from `odin-vs-extension-brief.md` during the planning and toolchain-exploration cycles. `CLAUDE.md` holds the full, current detail (conventions, test tiers, checklists); this file is the "what changed and why" for later reference.

## Decisions changed

| Area | Original brief | Now | Why |
|---|---|---|---|
| Extension model | Hybrid: new model for LSP + VSSDK | **VSSDK-only**, LSP via `ILanguageClient` | Hybrid runs the new-model code in-proc, losing its crash isolation, so it adds complexity for no gain. |
| Install target | `[17.0,)` | `[18.0,19.0)` | Personal use on VS 2026 only. |
| Project system | MPFProj vs CPS undecided; "study ZigVS's MPFProj" | **CPS-first timeboxed spike, MPFProj fallback** | 100+ project goal and Configuration Manager favor CPS (async, scalable). MPFProj is synchronous and UI-thread heavy. |
| Open Folder | Out of scope | Rejected explicitly | Solution-based workflow mixing C#/C++/Odin is the point. |
| Build logic | `.props/.targets`, no custom task | Targets plus a task in `OdinVS.Core` (`netstandard2.0`) | Makes arg building, reference closure and cycle detection unit-testable and identical in VS and command line. |
| `ProjectReference` | Open question: ordering vs collections | **Source refs = `-collection`, Link refs = linker input**, transitive, cycle-checked | Odin compiles a package plus its imports from source in one invocation; there is no per-library artifact. |
| ols bundling | "Bundled, no path setting" | Bundle ols, but **Odin must still be installed** (and located) | ols needs Odin's core libraries (unverified; test in M0). |

## Confirmed requirements
- `.sln`/`.odinproj`, mixed with C# and C/C++ projects in one solution.
- Configuration Manager (Debug/Release/...); Solution Explorer `Add Project`, `Add Existing Project`, `Add Item`, `Add Reference`.
- `.odinproj` XML is the source of truth; no property pages.
- All builds are native `odin` commands; circular references rejected with the cycle path.
- 10-20 projects typical, 100+ without pain.

## Verified facts (this machine)
- Odin `dev-2026-10-nightly:84bc3fc` at `C:\Program Files\Odin\odin\dist\odin.exe`; ols `dev-2026-10` at `C:\Program Files\Odin\ols\`.
- ols has no `--version`, speaks LSP on stdio, and blocks on stdin.
- `odin build -json-errors`: JSON on stderr, exit 1, no error codes, syntax errors suppress semantic errors, column can be 0.
- `-export-dependencies:json` lists source files (205 including Odin core); filter to project files for incremental inputs.
- `-debug` produces a PDB and implies `-o:none`.
- Samples compile: `samples/HelloOdin/src/main.odin` imports `hellolib:greeter` from `samples/HelloLib/greeter/lib.odin` via `-collection:hellolib=<samples>\HelloLib`.

## Corrections to the original brief
- Build errors need `-json-errors` (or a regex) to reach the Error List; Odin's default format is not MSBuild canonical.
- `$(OdinTargetsPath)` has no defined source; the VSIX install path is randomized. Decide in M0.
- Odin links through MSVC; command-line builds need a Developer shell.
- The Microsoft project-system blog cited is a VS 2015 preview and says MPFProj takes "many engineer-months", not weeks. The `microsoft/VSProjectSystem` docs are the working reference but stop at VS 2019 breaking changes.

## Added to the plan
- New `OdinVS.Core` library and MSTest projects; a fake `odin.exe` for MSBuild tests.
- Five test tiers (unit, ols protocol, MSBuild, VSIX smoke, VS integration) and per-milestone checklists, plus an M0 spike milestone before production code.
- C# conventions: nullable + warnings as errors, file-scoped namespaces, explicit types, permissive parameter types, collection expressions, no `async void`, `internal` by default, primary constructors, no `#region`, composition over inheritance, `sealed` by default.

## Open
- CPS vs MPFProj (decided by the spike).
- `OdinTargetsPath` resolution.
- Collection root and import convention.
- Warnings in `-json-errors`, remaining `odin build -help`, `odin check -help`.
