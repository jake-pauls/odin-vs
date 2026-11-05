# Concord visualizer spike (M5.5): findings

Branch `m5-5/debugging-experiments`. Tags: **verified** (seen on this machine), **sourced** (a public page, URL given), **unverified** (inference or background knowledge). Machine: VS 18.11.1 Community, installed engine assembly 18.11.10929.2.

## Phase A: feasibility (2026-11-04)

### A1 How VS loads a Concord extension: feasible
- A component is a DLL plus a compiled `.vsdconfig` (authored as `.vsdconfigxml`, compiled by `vsdconfigtool.exe`). The config lists a component GUID, the implementing assembly and class (managed: a public class with a default public constructor), a component level, and the interfaces each class implements, each with a **filter**. *Sourced:* wiki `Component-discovery-and-configuration`, `Hello-World-Sample` (https://github.com/microsoft/ConcordExtensibilitySamples/wiki).
- Shipping: a VSIX asset `<Asset Type="DebuggerEngineExtension" Path="X.vsdconfig" />` (the manifest asset type name is from the wiki's `Installing-Concord-Extensions`; **unverified** on 18.11 until B1). Components with level above 100,000 are IDE-side (in `devenv`); lower levels run in `msvsmon`. Our components are IDE-side, so no `msvsmon` deployment is needed for local debugging. *Sourced:* `Component-Levels`, `Installing-Concord-Extensions`. Remote debugging would need the DLL next to `msvsmon.exe` (not a goal).
- VS ships its own: **verified** compiled `.vsdconfig` files exist for the C++ engine (`Common7\IDE\CommonExtensions\Microsoft\VC\Debugger\cppdebug.vsdconfig`), the C#/VB evaluators (`...\VBCSharp\ExpressionEvaluators\*.vsdconfig`), a third-party managed one (`...\Visual Studio Tools for Unity\SyntaxTree.VisualStudio.Unity.Concord.vsdconfig`, which registers a `FramePseudoLocalResultProvider` and a CLR result provider) and `BlueprintConcordExtension`. They are binary; `vsdconfigtool` is not in the VS install.
- The public samples target VS 2022; their VSIX example uses `InstallationTarget [17.0,18.0)` and `<ProductArchitecture>amd64</ProductArchitecture>`. Nothing public mentions 18.x. Our VSIX already targets `[18.0,19.0)`. **Unverified** whether anything else changed in 18.x; B1 answers it.
- Logging aids: `Debug.SetLogging /DebugEngineAPIMethod` (17.8+) or, for Exp, `vsregedit set ... Exp HKLM Debugger EngineDiagEnableState DWORD 2`; the log shows each component call and "Skipped ... for 'X' filter", which is how to find out why a component is not called (`%TMP%\vsdebugeng.dll.log`). *Sourced:* `Getting-troubleshooting-logs`.

### A2 Which hook: feasible with caveat (the open risk is delegating to the C++ evaluator)
**Verified** by reflection on `Microsoft.VisualStudio.Debugger.Engine.dll` (18.11; .NET Framework 4.0 runtime, 3,428 types). The relevant interfaces and their methods:
| Interface | Methods | Use for us |
|---|---|---|
| `IDkmCustomVisualizer` | `EvaluateVisualizedExpression`, `UseDefaultEvaluationBehavior`, `GetChildren`, `GetItems`, `SetValueAsString`, `GetUnderlyingString` | Per-expression visualizer, the native-debuggee path the C++ sample uses (it is triggered by a natvis rule carrying a `VisualizerId`, sourced). Candidate for slices and dynamic arrays. |
| `IDkmFramePseudoLocalResultProvider` | `GetResult`, `GetChildren`, `GetItems`, `GetUnderlyingString` | Adds pseudo-locals (`$exception`-style); not a way to restyle real locals. |
| `IDkmLanguageExpressionEvaluator` | `EvaluateExpression`, `GetChildren`, `GetFrameLocals`, `GetFrameArguments`, `GetItems`, `SetValueAsString`, `GetUnderlyingString` | The Odin-native evaluator: Locals, Watch, expansion all go through it. Needed for Odin expression syntax and type names. |
| `IDkmSymbolCompilerIdQuery` | `GetCompilerId(DkmInstructionSymbol, DkmInspectionSession)` | Lets a component say which compiler produced a frame; the routing key for choosing an evaluator per language. |
| `IDkmLanguageInstructionDecoder` / `IDkmLanguageFrameDecoder` | `GetMethodName` / frame name | Call Stack names (`pkg.proc`). |
- The C++ engine itself is native components (`CppEE::CGroupEE`, `CCppExprEval`, `CFallbackEE`, `CNativeTypeSystem` seen in `cppdebug.vsdconfig`). `CGroupEE` implements `IDkmGroupLanguageExpressionEvaluator` (name from the engine's types), which is the dispatcher over per-language evaluators. That suggests the architecture: our evaluator is registered for a language/compiler ID and the group evaluator routes frames to it.
- **Caveat 1 (unverified, the main risk):** whether a component can wrap the C++ evaluator, for example evaluate `values.data` with the C++ EE and then re-present the result, or call it for children. The wiki documents method chaining to "the next implementation" (the dispatcher object passed as first parameter) but no page says the built-in native EE is a chain target, and it recommends *extending* Microsoft's managed EE over replacing one. If delegation does not work, a visualizer can still read memory itself (`DkmProcess.ReadMemory`, shown in the C++ sample) and build children, but arbitrary Watch expressions in Odin syntax would then need our own parser (the evaluator route's real cost).
- **Caveat 2 (unverified):** the Odin PDB's language and producer. The Call Stack Language column showed `C`, so the PDB probably carries C and an LLVM/`odin` producer string, and the C++ EE claims every frame. To route Odin frames to our evaluator (and keep C/C++ projects unaffected, Phase F) we would answer `IDkmSymbolCompilerIdQuery` for modules whose producer says Odin, or filter on module path/name. Which of these works is a B2 question: log what the PDB's compiler info is.
- **Caveat 3 (sourced):** the managed EE sample notes that Autos and DataTips need a language service, so a Concord-only language shows nothing there. Whether this applies when the language is C (existing service) and we only replace the evaluator is **unverified**.
- Natvis-driven path: `IDkmCustomVisualizer` is activated through natvis, and natvis rejects `[]int` as a type name (M5). A visualizer keyed on a natvis rule would therefore need a natvis type it can match (for example `*`, which natvis also rejected), so the evaluator route, which sees every value, is more promising for Odin-named types; the visualizer route stays for `string`-like C-shaped types. **Unverified**; C and D test it.

### A3 Dependencies and build: feasible
Public NuGet packages exist for the VS 18 line (read from the nuget.org flat container, versions as of 2026-11-04). None is in the local cache; **none restored yet**.
| Package | Role | Latest versions |
|---|---|---|
| `Microsoft.VisualStudio.Debugger.Engine` | managed reference assembly for the Concord API | 17.14.1051801; 18.0.1082202-preview |
| `Microsoft.VSSDK.Debugger.VSDConfigTool` | `vsdconfigtool.exe` + MSBuild targets (`Microsoft.VSSDK.Debugger.VSDConfigTool.targets` for VS 2019+) | 17.14.1051801; 18.0.1082202-preview |
| `Microsoft.VisualStudio.Debugger.Metadata` | needed only for managed *result provider* components (CLR) | same line; probably not needed for native |
| `Microsoft.VSSDK.Debugger.VSDebugEng` | native headers/lib; only for a C++ component | not needed (we write managed) |
Plan: restore `Microsoft.VisualStudio.Debugger.Engine` and `Microsoft.VSSDK.Debugger.VSDConfigTool`, both **17.14.1051801** (latest stable; the 18.0 line is preview and this project already pairs 17.14 packages with VS 18.11 successfully). The fallback is a `HintPath` to the installed `Microsoft.VisualStudio.Debugger.Engine.dll` (18.11, present at `Common7\IDE`) with `vsdconfigtool` still needing the package. Target framework for managed components is **unverified**; the installed assembly is built for the .NET Framework 4.x runtime, so `net472` like the other VSIX projects. The component is a separate class library referenced by the VSIX, with `DebuggerEngineExtension` as an extra asset.

### A4 Gate result: **GO, nothing impossible**
- A1 feasible, A2 feasible with caveats 1-3, A3 feasible. No "impossible" found, so Phase B can start once the package restore is approved.
- The decisive unknowns, to be answered by B1/B2/C rather than more reading: (1) asset type and load on 18.11; (2) the compiler/language information the Odin PDB gives (routing key); (3) whether the C++ evaluator can be delegated to; (4) whether Autos/DataTips still work when our evaluator is involved.
- If (3) is "no", the likely outcome of the milestone is: a visualizer (memory reads) can deliver slices/dynamic arrays and type names; Odin expression syntax needs a parser of our own, which is the evaluator-milestone cost to estimate in G4.

## Phase B: walking skeleton (2026-11-04, Exp 18.11, spike VSIX 0.0.1 and 0.0.2; user ran F5, evidence is `%LOCALAPPDATA%\OdinVS\concord-spike.log` and a Locals screenshot)

### B0 Sample: passes
`sample/ConcordSample.sln` + `src/main.odin` build with real `odin` (`-debug`) via `Odin.Sdk` 0.3.3-local and F5 stops at the breakpoint. Covers `[]int`, `[]string`, empty and nil slices, `[dynamic]int|string`, an empty dynamic array with cap 8, a 100,000-element one, `[]Thing` (struct with a slice and a dynamic array), and the stretch types.

### B1 Component loads: passes
- The spike VSIX (`DebuggerEngineExtension` asset + the compiled `.vsdconfig` + `OdinConcordSpike.dll`, net472, built with VS MSBuild) installs into Exp next to the product VSIX; the component is constructed inside Exp's `devenv` (the process id in the log is devenv's) on the first debug event, and the log is empty until a debug session starts. So the asset type is right on 18.11 and the component is in-proc.
- Packaging gotcha: `vsdconfigtool` writes `bin\...\X.vsdconfig` but the VSIX did not contain it until it was added as `<Content ... IncludeInVSIX=true>`; check the `.vsix` zip listing.

### B2 What the component sees: passes
- **Module compiler id is empty:** `ConcordSample.exe` loads with symbols and `CompilerId` vendor/language both `Guid.Empty`; the debugger resolves language per symbol (`IDkmSymbolCompilerIdQuery`), and the frames report `DkmLanguageId.C`.
- **Delegation to the C++ evaluator works (A2 caveat 1 answered):** a `[LanguageId = DkmLanguageId.C]` `IDkmLanguageExpressionEvaluator` at component level 9992100 is called before the C++ EE for `GetFrameLocals`, `EvaluateExpression` and `GetChildren`; chaining with `inspectionContext.GetFrameLocals(...)`, `.EvaluateExpression(...)`, `result.GetChildren(...)` reaches the native evaluator and Locals/Watch looked normal to the user (pass-through). This also means a component can read the native EE's `DkmSuccessEvaluationResult`s (Name, Type, Value, FullName, Flags, Address) and replace them.
- **Type names the PDB/EE hand us** (28 locals): composites are already Odin-shaped: `[]int`, `[]string`, `[]main::Thing`, `[dynamic]int`, `[dynamic]string`, `map[int]string`, `map[string]int`, `bit_set[main::Flag]`, `matrix[2, 2]f32`, `main::Shape`, `main::Color`, `runtime::Context %`, `any`. Scalars are C names: `__int64`, `float`, `bool`, `char32_t` (rune), `char *` (cstring), `__int64 *` (`^int` and `[^]int`), `__int64[4]` (`[4]int`), `unsigned char`.
- **Display today:** slice `{data=0x... {10} len=3 }`, dynamic `{data=... {1} len=4 cap=4 ...}`; `flags` bit_set already shows `{Read=true Write=false Exec=true }`; `color` enum shows `Green (1)`; `fixed` shows `{5, 6, 7, 8}`; `shape` union shows `tag` plus every variant; `names`/`scores` maps show garbage keys and values (the map data pointer carries packed bits); `any` shows `{data=... id=...}`; `text` string is `Visualized` by `odin.natvis`.
- **Watch syntax:** `ints[1]` fails in the C evaluator (error text "no operator [] matches these operands"); a translation layer (Odin expression to `ints.data[1]`, chained to the C++ EE) is the cheap route to Odin expressions.
- Open for C: whether results carry a usable `Address` (for memory reads), Autos and DataTips.

### Display notes (open, cosmetic)
- **`::` in type names is the PDB's spelling, not Odin's.** Odin writes `main.Thing`; the PDB (and so the native EE, before any of our code) says `main::Thing`, `main::Color`, `bit_set[main::Flag]`. A display rename `::` to `.` in the evaluator's type strings is possible (we already rewrite scalar type names) but is not done; decide whether it is wanted when choosing what a product debug component shows.

### Thread rule found the hard way (0.0.5 -> 0.0.6)
- 0.0.5 retried failed Watch expressions from a thread-pool thread. Result: `DkmLanguageExpression.Create` threw `DkmException: Error in the application` (`DkmDataContainer.BeforeCreateCall`), and the exception handler / watchdog then completed the evaluation from the foreign thread; devenv crashed within seconds on `things[0].tags[1]`, `strs[2]`-style rewrites and even `nosuchvar[0]` (user report, 2026-11-04). Best explanation (the log shows the exception, the crash itself left no trace): Concord objects and completion routines must only be used on a thread registered with `DkmComponentManager.InitializeThread`. 0.0.6 registers the retry and watchdog threads and catches everything that could escape them. Unverified until re-run in Exp.

### 0.0.6 result and 0.0.7 (flat fan-out)
- 0.0.6 (threads registered with `InitializeThread`) did not crash, but `things[0].tags[1]` still showed "Evaluating expression..." forever. The log shows the rewritten `things.data[0].tags.data[1]` DID evaluate (third nested call), yet the watchdog fired ~14,000 times and the UI never got an answer. So the `InitializeThread` theory was not confirmed as the crash fix, and completing from our own timer thread does not reach the UI. Do not complete evaluations from threads we create.
- 0.0.7: no threads, no timer. From the first failure's callback all subsets of `.data` insertions (at most 16) are issued in one flat batch and the first success in subset order wins; nesting depth is two, which worked in 0.0.4. Unverified until re-run.
- **0.0.7 verified in Exp (user, 2026-11-04):** `len(strs)`=3, `strs[1]`="beta", `things[0].tags[1]`="y", `things[0].items[2]`=30 (all rewritten to `.data[...]`), `nosuchvar[0]` shows `identifier "nosuchvar" is undefined` quickly; no hang, no crash. The flat fan-out works.

### E1 map[K]V: works (spike 0.0.8, verified in Exp by the user 2026-11-05)
- `scores` (`map[string]int`) and `names` (`map[int]string`) show `{len=2}` and expand to `[len]` plus `["ann"]=90`, `["bob"]=75` / `[1]="one"`, `[2]="two"` in Locals and Watch; values of type `string` expand further. The log's computed addresses match the layout (cap 8, keys 0x...d40, values +0x80 = 8 slots x 16 bytes, hashes +0x40).
- Method: parse `map[K]V` from the type name; ask the C++ evaluator for `<map>.data`, `sizeof(K)`, `sizeof(V)` (one flat batch); cap = `1 << (data & 63)`, base = `data & ~63`; keys, values, hashes are consecutive arrays of 64-byte cells (offset formula from `dynamic_map_internal.odin`); read the hash array with `Process.ReadMemory`; a slot is used when hash != 0 and bit 63 is clear; each entry is evaluated lazily as `*(K*)0xaddr` / `*(V*)0xaddr` with the C++ evaluator, so values keep their native display and expansion. The map header (`data`) comes from an expression because results carry no address.
- Cost to build: one session step, about 300 lines, no new APIs beyond slices; the layout is stable Odin runtime detail, so it must be re-checked on each Odin bump (pin it like ols).
- Phase F data point: casting to the Odin `string` type (`*(string*)0x...`) works in the C++ evaluator; no collision with C++ `string` was seen in this sample.
- Untested: entry sizes that do not divide 64 (padding formula), struct keys/values, tombstones after `delete`, maps larger than 8 slots, nested maps.

## Odin-version-coupled code (check first on every Odin bump; user request 2026-11-05)
Anything that depends on Odin's base library or compiler output rather than on the PDB's type names must be listed here, so a version bump starts with this list.

| Visualization | Depends on | Where in Odin | Breaks if |
|---|---|---|---|
| `map[K]V` entries | `Raw_Map` field order (`data`, `len`, `allocator`); `data` = pointer with log2 capacity in the low 6 bits; keys, values, hashes laid out as consecutive `Map_Cell` arrays of 64-byte cells; slot used when hash != 0 and top bit clear; `Map_Hash` = `uintptr` | `base/runtime/core.odin` (`Raw_Map`, `Map_Cell`, `MAP_CACHE_LINE_SIZE`, `Map_Hash`), `base/runtime/dynamic_map_internal.odin` (`map_cell_index_*`, `map_kvh_data_dynamic`, `map_hash_is_valid`) | any of those change |
| Slices and dynamic arrays | `len` at offset 8, `cap` at offset 16, member named `.data` and `.len` in the PDB struct (`{data, len, cap, allocator}`) | `Raw_Slice`, `Raw_Dynamic_Array` in `core.odin` | field names or order change |
| Type names | PDB spelling of composites (`[]T`, `[dynamic]T`, `map[K]V`) and of scalars (`__int64`, `char32_t`, ...) | Odin's CodeView debug-info emitter | the emitter renames or splits types |

Planned guard (proposed, not built): (1) a tier-2-style test, `RequiresToolchain`, that reads the installed Odin's `base/runtime` sources and asserts the exact constructs above exist (cheap, no debugger); (2) an Odin program that dumps a map's real layout (`runtime.map_kvh_data_dynamic` offsets and raw hashes) which a C# unit test of the decoding math compares against, so a layout change fails without Visual Studio; (3) the Exp checklist re-run on the `ConcordSample` for the end-to-end display.

### E1 follow-up: struct keys/values, truncated headers, cast spelling (spikes 0.0.9-0.0.11, verified in Exp by the user 2026-11-05)
- Works: map[Triple]int (proc-local type, 12-byte keys, cell padding), map[Pair]string, map[int]Thing (80-byte values), map[int]int with 12 entries and two deleted keys, plus the earlier maps; Locals and Watch; struct keys show as [{a=4 b=5 c=6 }], struct values expand.
- Truncated C++ value text (0.0.9): long values are cut (len=...), so len/cap parsed from text failed and the raw C view showed. Fixed by evaluating <name>.len / .cap with the C++ evaluator when the text has no number (skipped in the Watch [] fallback, already two evaluations deep).
- Sizes without type names (0.0.10): sizeof(<map>.data->key) / ->value work for any key/value, including proc-local types.
- Layout correction (0.0.11): the array after a cell array starts at ElementOffset(capacity) (map_cell_index_dynamic), not after whole cells; 12-byte keys at capacity 8 give 100 bytes. Wrong math showed as 7 used slots instead of 3.
- Cast spelling: *(main::Thing*)0x.. is rejected by the C++ evaluator; *(::main::Thing*)0x.. (leading global-scope ::) is accepted, also for proc-local main::main::Triple::$585. string and __int64 need no prefix.
- Untested: nested maps, maps as values, V as a slice, maps in other packages (pkg::Type, same :: prefix expected). Entry order is slot (hash) order.

### Package-level variables (Watch only; Locals never lists them) - verified in Exp by the user 2026-11-05
- Plain names work in Watch once the variable has a debug symbol: g_count (41), g_name ("global"), g_ints ({len=3}), g_dyn ({len=3 cap=8}), g_map ({len=2}), g_things ({len=1 cap=8}), g_const_count (constant initializer, 41); g_ints.data[1] and g_ints.len work, and editing g_ints.data[1] to 9 worked. They go through the same slice/map display as locals.
- main::g_ints / main::g_count are rejected by the C++ evaluator ("name followed by :: must be a class or namespace name"); package-level names are plain identifiers.
- First run: g_count, g_name and g_ints were "identifier is undefined" while g_dyn, g_map and g_things were visible. My guess (constant initializer on the declaration means no debug symbol) was WRONG: after the initializers moved into main the constant-initialized control g_const_count was visible too. What also changed in that edit is that all of them became referenced (printed); the first-run failures were the three globals that were declared but never used. Most likely cause: Odin emits no debug symbol for a package-level variable nothing references. Not isolated (two changes at once); the native evaluator, not our component, reports the error.
- Consequence for users: an unreferenced global cannot be inspected; not fixable from a debug component.
