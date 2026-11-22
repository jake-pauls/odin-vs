# Debugging in Visual Studio

Press F5 (or Ctrl+F5) on an Odin project; a Debug build carries `-debug`, so breakpoints in `.odin` files bind. The extension also ships a debugger component that makes Odin values readable in **Locals**, **Watch** and data tips (Autos shows the native display for now). It only acts on Odin programs (a module whose debug symbols name `.odin` sources), so C and C++ debugging in the same solution is unchanged.

**What the displays show**

| Odin value | Shown as |
|---|---|
| `[]T` slice, `[dynamic]T` | `{len=N}` / `{len=N cap=M}`; children `[len]`, `[cap]` (dynamic only) and the elements |
| `string` | the text in quotes (cut at 256 bytes, then `...`); `data` and `len` stay available as children |
| `map[K]V` | `{len=N}`; children `[len]` and one `[key]` entry per item (keys may be strings, numbers or structs); a set `map[K]struct{}` lists `[key] {}`; at most 1000 entries are listed, with a `[shown] first 1000 of N` row |
| `union` | the active variant only |
| `matrix[R, C]T` | one row per line, with a `[storage]` row (`column-major` by default, `row-major` for `#row_major`) |
| `int`, `u8`, `f32`, `rune`, `cstring`, ... | Odin names for the scalar types (the debug symbols cannot tell `int` from `i64`, so `int` is a best guess) |
| `enum`, `bit_set`, `[N]T` | already readable without the component |

Not covered: `any`, `Small_Array`, `#soa` arrays and `Maybe` of a pointer (it has no tag, so it shows as the native `{v0=...}`). Pointers `[^]T` display as plain pointers.

**Expressions you can type in Watch** (tried in Visual Studio unless noted)

| Expression | Result |
|---|---|
| `len(x)`, `cap(x)` | the length or capacity of a slice, dynamic array or string (`cap` for dynamic arrays); shown as `x.len`, `x.cap` |
| `x[i]` | element `i` of a slice, dynamic array or string; shown as `x.data[i]`. An index below 0 or at or above `len` is an error that names the length, for example `index 99 is out of bounds of ints (length 3)` |
| `a[i]` on a fixed array `[N]T` | the element; an out-of-range index is an error with the length |
| `x[i].field`, `x[i].list[j]` | every index in the expression is checked |
| `m[key]` on a `map[K]V` | the value stored under `key`. `key` can be a literal (`m["red"]`, `m[5]`), a variable (`m[name]`, `m[point]`), or an expression (`m[key + 1]`, `m[names[0]]`, `m[len(x)]`); for a set the result is `{}`. A key that is not in the map is an error (`key "zzz" not found in m (2 entries)`) |
| `m[key].field`, `m[key].list[i]` | a field of the value found, shown decoded; indexes get the same range check |
| `m[key]` where the value is a slice, dynamic array or map | the decoded value (`{len=3}`, expandable); `m[key].len` works, and `m[key].cap` and `m[key][i]` are implemented but not yet tried in Visual Studio (the element type must be nameable); nothing deeper, and not a lookup inside such a map |
| `x.data[i]`, `x.len` | the real fields, unchecked |

Limits: a map key expression cannot itself contain a map lookup (`m[n["a"]]`). A number that does not fit the key type (`m[300]` on a `map[u8]V`) is reported, not wrapped around. Struct keys are compared byte for byte, padding included. An index with side effects (`a[i++]`, a call) is evaluated as typed and not range-checked. `len(a) + len(b)` and `len` inside an index (`m[len(k)]`) work. A map is searched entry by entry in the debuggee's memory: fine for tens of thousands of entries, and the search stops after 200,000 string comparisons or on a map over a million slots, saying so. Type names from other packages work (`shapes.Point`). `x[i]` on a `[^]T` is C-style and not range-checked, because the pointer has no length.

If a display looks wrong, Visual Studio shows the native `{data, len}` value instead, and `%LOCALAPPDATA%\OdinVS\odinvs-debugger.log` says why (it rotates at 1 MB). If the component fails to load after a Visual Studio update, strings and slices fall back to that native display.
