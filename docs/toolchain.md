Referenced from `CLAUDE.md`. Facts verified on this machine; re-verify when Odin or ols is bumped.

# Verified toolchain facts (this machine)
- `odin version` -> `dev-2026-10-nightly:84bc3fc`; `odin root` -> `C:\Program Files\Odin\odin\dist\`.
- ols logs `Starting Odin Language Server dev-2026-10`, speaks LSP over stdio with `Content-Length` framing, has **no `--version` flag**, and **blocks reading stdin**. Version checks must come from that log line or the file's product version, never from running it bare. Always give it stdin that closes, or kill it on a timeout.
- Flags that change the design:
  - `-json-errors`: structured diagnostics (see finding 8).
  - `-export-dependencies:json` + `-export-dependencies-file:<f>`: the compiler lists every source file it used, so incremental-build inputs can come from the previous build rather than from globs (also covers sources reached through collections).
  - `-show-import-graph`: DOT import graph, useful for debugging reference closures.
  - `-debug` implies `-o:none`; `-pdb-name` controls the PDB name; `-build-mode` supports `lib`/`static`, `dll`, `obj`, `test`; `-collection:name=path` is available for `import "name:pkg"` and `foreign import`.
  - `-error-pos-style:unix` exists but is also not MSBuild canonical.
- **Captured with the samples** (the two throwaway sample packages `samples/HelloOdin` and `samples/HelloLib`, removed from the repo; see commit `3e00cc6`; `HelloOdin` imported `hellolib:greeter` from `HelloLib`; build with `-collection:hellolib=<samples>\HelloLib`):
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

