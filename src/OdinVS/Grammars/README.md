# Grammars

`odin.tmLanguage.json` is the Odin grammar from DanielGavin/ols with **one tagged local block added**; do not otherwise edit it. Licenses are in `THIRD-PARTY-NOTICES.md` at the repo root.

| | |
|---|---|
| Source | https://github.com/DanielGavin/ols, `editors/vscode/syntaxes/odin.tmLanguage.json` |
| Pinned commit | `d26a19b38c581e28f67609cbb2d5ac933a9373d1` (2025-01-13) |
| SHA-256 of upstream file | `d2a4331a0912a9bdf9b361596c6ce50f403eaa2ad686cdccc2f5d3516a85ca3d` |
| SHA-256 of our file | `e2a4f1d2c9803846e18690f3a25ebc27715f1c440e42e0e638de34e84e13ae07` |

## Local divergence (the only one)
Add `"fileTypes": ["odin"],` on the line after `"name": "Odin",`, wrapped in `// ODIN-VS(START)` and `// ODIN-VS(END)` comment lines (VS parses the grammar with Newtonsoft, which accepts `//` comments; checked). Any future divergence gets the same tags, so `grep ODIN-VS` lists every local change.

Why: VS decides which files a grammar applies to from the grammar's own `fileTypes` (a grammar with none gets no extensions; verified by decompiling `Microsoft.VisualStudio.TextMate.Core`, `JsonGrammarLoader`, and by the grammar cache showing `.odin` only after the line was added). The VS Code extension this grammar comes from maps `.odin` in its `package.json` instead, so upstream has no such field.

`language-configuration.json` is ours (not copied from ols).

## Updating the grammar
1. Pick a new commit that touched the file and fetch it: `gh api -H "Accept: application/vnd.github.raw" "repos/DanielGavin/ols/contents/editors/vscode/syntaxes/odin.tmLanguage.json?ref=<sha>"`.
2. Diff it against the checked-in file; the only expected difference is the tagged `ODIN-VS` `fileTypes` block plus whatever upstream changed.
3. Re-apply the tagged `fileTypes` block, update the SHA, date and both hashes here, and the commit and date in `THIRD-PARTY-NOTICES.md`, then re-run the fixture check from `docs/internal/milestones.md` (M1) under Light and Dark.
