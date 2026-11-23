# Spikes

Throwaway investigations from early in the project, kept as a record of how the main decisions were reached. Each one tested a single question in isolation before any product code was written.

> **These are not maintained and may not build or run any more.** They were written against Visual Studio 18.11 and the Odin and `ols` versions of late 2026, for one-off experiments in the Experimental instance. Some contain hard-coded paths (the local ones were replaced with placeholders or relative paths when the repository was prepared for publication, so a spike that depended on them needs a path fixed first). Nothing in `src/` or `tests/` depends on them, and they are not part of the product build or the test suite.

| Folder | Question it answered | Outcome |
|---|---|---|
| [`cps-feasibility/`](cps-feasibility/FINDINGS.md) | Can Visual Studio's Common Project System host an `.odinproj` project type (load, file tree, configurations, Add Item, F5)? | Yes; CPS was chosen for the project system |
| [`odin-breakpoints/`](odin-breakpoints/FINDINGS.md) | Do breakpoints in `.odin` files bind when an `odin -debug` executable runs under Visual Studio? | Yes |
| [`odin-targets-path/`](odin-targets-path/FINDINGS.md) | How does a project find the Odin build targets when the extension installs into a random folder? | The `Odin.Sdk` MSBuild SDK, packed locally |
| [`concord-visualizer/`](concord-visualizer/FINDINGS.md) | Can a Concord debugger component show Odin slices, maps and strings readably? | Yes; promoted into `src/OdinVS.Debugger/` (the spike copy stays as it was) |

Each `FINDINGS.md` records what was tried, how each claim was verified and what did not work. The checklists that tie the spikes to the product milestones are in [docs/internal/milestones.md](../docs/internal/milestones.md).

The spikes are frozen history: they are left as they were, apart from the path changes above. New experiments go under `spikes/<name>/` on their own branch; promote anything worth keeping into `src/` by copying it.
