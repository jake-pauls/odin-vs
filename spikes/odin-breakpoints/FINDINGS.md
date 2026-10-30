# Odin breakpoint gate (M0)

Date: 2026-10-29. Branch `m0/odin-breakpoints` (cut from `m0/cps-feasibility`, which is not yet merged to the milestone branch).

## Method
1. `odin build src -debug -strict-style -out:bin/bp.exe` in this folder (`src/main.odin`: a proc `add` called in a loop from `main`). Produces `bp.exe` + `bp.pdb`; the exe runs and prints `total: 10 odin`.
2. The PDB mentions `...\odin-breakpoints\src\main.odin` (grep of the file), so source paths are recorded.
3. Main VS 18.11 instance started with `devenv /debugexe bp.exe src\main.odin` (Open as project, no `.odinproj`). Breakpoint set by clicking the margin of line 6 (`sum := a + b`), then Debug > Start Debugging.

## Result: PASS
- The breakpoint glyph was solid red before launch and the debugger **stopped on line 6** (current-line arrow on `sum := a + b`).
- Call Stack: `bp.exe!main::add(__int64 a, __int64 b, runtime::Context *) Line 6`, then `bp.exe!main::main(runtime::Context *) Line 13`, then the CRT `main(int argc, char **argv) Line 78`.
- Modules window: `bp.exe`, Symbol Status "Symbols loaded", Symbol File = the PDB beside the exe.
- Locals shows `a`, `b` as `__int64` (value 0 on first hit), plus the implicit `context` (`runtime::Context`). Odin `int` surfaces as `__int64`, the language shows as C. Strings, slices and structs were **not** inspected; that is M5.
- Stopped with Stop Debugging; the temporary solution was not saved.

## Notes for M5
- Debugging works with the native engine and no Odin-specific VS support; the F5 provider from the CPS spike (`LaunchDebugEngineGuid` native) is sufficient.
- The editor used for the `.odin` file was plain text (no grammar yet); breakpoints do not need a language service.
- A first command line failed to pass `/debugexe` from Git Bash (the shell rewrote it to `C:/Program Files/Git/debugexe`), yet VS still opened the exe and file; use PowerShell or `//debugexe` from Git Bash.
- Not tested: Release (no PDB), breakpoints bound while attached from a CPS project (F5 path), conditional breakpoints, stepping.
