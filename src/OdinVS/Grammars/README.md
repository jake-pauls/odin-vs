# Grammars

`odin.tmLanguage.json` is a verbatim copy of the Odin grammar from DanielGavin/ols; do not edit it. Licenses are in `THIRD-PARTY-NOTICES.md` at the repo root.

| | |
|---|---|
| Source | https://github.com/DanielGavin/ols, `editors/vscode/syntaxes/odin.tmLanguage.json` |
| Pinned commit | `d26a19b38c581e28f67609cbb2d5ac933a9373d1` (2025-01-13) |
| SHA-256 | `d2a4331a0912a9bdf9b361596c6ce50f403eaa2ad686cdccc2f5d3516a85ca3d` |

`language-configuration.json` is ours (not copied from ols).

## Updating the grammar
1. Pick a new commit that touched the file and fetch it: `gh api -H "Accept: application/vnd.github.raw" "repos/DanielGavin/ols/contents/editors/vscode/syntaxes/odin.tmLanguage.json?ref=<sha>"`.
2. Diff against the checked-in file to see what diverged.
3. Update the SHA, date and hash here and in `THIRD-PARTY-NOTICES.md`, then re-run the fixture check from `docs/milestones.md` (M1) under Light and Dark.
