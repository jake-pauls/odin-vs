# Third-party notices

odin-vs includes the following third-party material. It is also shipped inside the VSIX.

## Odin TextMate grammar

- File: `src/OdinVS/Grammars/odin.tmLanguage.json`, unmodified.
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
