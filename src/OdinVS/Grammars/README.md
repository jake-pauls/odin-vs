# Grammars

`odin.tmLanguage.json` is the Odin grammar from DanielGavin/ols with **one local line added**; do not otherwise edit it. Licenses are in `THIRD-PARTY-NOTICES.md` at the repo root.

| | |
|---|---|
| Source | https://github.com/DanielGavin/ols, `editors/vscode/syntaxes/odin.tmLanguage.json` |
| Pinned commit | `d26a19b38c581e28f67609cbb2d5ac933a9373d1` (2025-01-13) |
| SHA-256 of upstream file | `d2a4331a0912a9bdf9b361596c6ce50f403eaa2ad686cdccc2f5d3516a85ca3d` |
| SHA-256 of our file | `ae8aab7209deafe0a8978e29dbf584097a1e510dacf0dc4d3be0fc348b8530b8` |

## Local divergence (the only one)
Add `"fileTypes": ["odin"],` on the line after `"name": "Odin",`.

Why: VS decides which files a grammar applies to from the grammar's own `fileTypes` (a grammar with none gets no extensions; verified by decompiling `Microsoft.VisualStudio.TextMate.Core`, `JsonGrammarLoader`, and by the grammar cache showing `.odin` only after the line was added). The VS Code extension this grammar comes from maps `.odin` in its `package.json` instead, so upstream has no such field.

`language-configuration.json` is ours (not copied from ols).

## Updating the grammar
1. Pick a new commit that touched the file and fetch it: `gh api -H "Accept: application/vnd.github.raw" "repos/DanielGavin/ols/contents/editors/vscode/syntaxes/odin.tmLanguage.json?ref=<sha>"`.
2. Diff it against the checked-in file; the only expected difference is the `fileTypes` line plus whatever upstream changed.
3. Re-apply the `fileTypes` line, update the SHA, date and both hashes here and in `THIRD-PARTY-NOTICES.md`, then re-run the fixture check from `docs/milestones.md` (M1) under Light and Dark.
