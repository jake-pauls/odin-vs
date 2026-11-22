# Known limitations

The debugger and reference features have these gaps. None of them regress C, C++ or C# debugging.

| Where | What happens |
|---|---|
| Autos window | Empty for `.odin` files, with or without the component (Visual Studio has no Odin language service to fill it) |
| Static library (`OdinOutputType` `Lib`) | `-build-mode:lib` fails to link on Windows with the tested Odin (`dev-2026-10`); a `Dll` works |
| Raw View | The displayed containers have no Raw View child to see the native fields; use `x.data` and `x.len` in Watch |
| `any`, `Small_Array`, `#soa` arrays, `Maybe` of a pointer | Shown natively (for `Maybe` of a pointer as `{v0=...}`) |
| `[^]T` | Plain pointer; `x[i]` works but is not range-checked, and there is no element list |
| `int` against `i64` | The debug symbols cannot tell them apart, so `int` is a best guess |
| Two map types with one debug-info name (for example `map[int]int` next to `map[int][dynamic]int`) | `m[key]` and the entry list report "cannot be searched" or "the layout does not fit" instead of a value, and which of the two fails can change between runs; open the other one in Locals |
| `m[key]` where the value is a union or matrix | Shown natively |
| `m[key][i]` and `m[key].cap` on a slice, dynamic array or map value | Implemented, not yet tried in Visual Studio |
| A lookup inside a lookup (`nested_map["outer"]["x"]`) and a key that contains a lookup (`m[n["a"]]`) | Error saying it is not supported |
| A slice or dynamic array whose element type has no nameable spelling (for example one holding a slice) | The value and length show, the elements are not listed |
| Map keys that are padded structs, or `[N]T` arrays of 8 bytes or less | Not tried; may report a mismatch |
| Maps of more than 1000 entries | Only the first 1000 are listed (`[shown]` row); `m[key]` still finds any entry |
| 32-bit Odin programs | Not tried |
| Package-level variables | Only reachable in Watch, and only when the compiler emitted a symbol |
| An index that has side effects (`a[i++]`, a call) | Evaluated as typed, no range check |
