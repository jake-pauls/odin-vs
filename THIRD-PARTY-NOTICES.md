# Third-party notices

odin-vs includes the following third-party material. It is also shipped inside the VSIX.

## Odin Language Server (ols), bundled binary
- Files: `src/OdinVS/ols/ols.exe` and `src/OdinVS/ols/builtin/builtin.odin`, `src/OdinVS/ols/builtin/intrinsics.odin`, unmodified. They are shipped in the VSIX under `ols\` and started by the extension as the language server.
- Source: https://github.com/DanielGavin/ols. The files were copied from the local install at `C:\Program Files\Odin\ols\` (`ols-x86_64-pc-windows-msvc.exe`, renamed to `ols.exe`), which reports `dev-2026-10` (see `BundledOls.Version`). The upstream commit it was built from is not recorded; it was not looked up.
- SHA-256: `ols.exe` `01ee56f9b67f1fa772a5708e763c92f3d99750e21a1deaecc5ccf2e1cd849f95`; `builtin.odin` `12cd885b0334c568960372dc88bd7b0f875defda71c55239284306fd999c58f4`; `intrinsics.odin` `f0bb8c90c7f27769b230e08c859d25492bee6ef3e3371cc38f76ae9d712126b1`. Recompute and update these whenever the bundled files change.
- License: MIT, Copyright (c) 2021 Daniel Gavin (text under "ols (Daniel Gavin)" below).

## Odin TextMate grammar

- File: `src/OdinVS/Grammars/odin.tmLanguage.json`. Modified by odin-vs: one block added (`"fileTypes": ["odin"],` between `// ODIN-VS(START)` and `// ODIN-VS(END)` comment lines) so Visual Studio associates the grammar with `.odin` files; see `src/OdinVS/Grammars/README.md`.
- Source: https://github.com/DanielGavin/ols, `editors/vscode/syntaxes/odin.tmLanguage.json` at commit `d26a19b38c581e28f67609cbb2d5ac933a9373d1` (2025-01-13).
- The grammar's own header says "Modification of https://github.com/fivemoreminix/odin-vscode". That name now resolves to https://github.com/lukewilson2002/odin-vscode (checked with `gh api` on 2026-10-30; `fivemoreminix/odin-vscode` and `asmoaesl/odin-vscode` return the same repository, which is not a fork).
- Both projects are MIT licensed. Their notices follow.

### ols (Daniel Gavin)

```
MIT License

Copyright (c) 2021 Daniel Gavin

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### odin-vscode (Luke Wilson)

```
MIT License

Copyright (c) 2018 Luke Wilson

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

Provenance notes: the odin-vscode repository history shows it was Unlicense before it was relicensed to MIT on 2018-06-12, and a 2018 commit ("Bring up to date with official") does not name its source. Later contributors to the grammar file in that repository include Tetralux and khvorov45.
