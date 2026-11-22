# References

A `ProjectReference` makes another project available to this one. (An Odin *collection* is a named import root: `import "name:path"` looks `path` up under the directory registered as `name`.) Odin has no separate compilation of libraries (building a project compiles its package and every package it imports, from source), so there are two kinds:

| `ReferenceKind` | Default for | What happens |
|---|---|---|
| `Source` | another `.odinproj` | The referenced project is compiled as source: its collection is passed as `-collection:<OdinCollectionName>=<OdinCollectionRoot>`, so you write `import "Lib:src/greeter"`. Collections are transitive: if `Lib` references `Base`, anything referencing `Lib` also gets `Base`'s collection |
| `Link` | a `.vcxproj` (C/C++) | The referenced project is built first and its library (`.lib`; for a DLL its import library next to the `.dll`) goes to the linker through `-extra-linker-flags`. Linking is also found through `Source` references, so `App` -> `Lib` (Source) -> `Native` (Link) links `Native` into `App` |

Build order comes from MSBuild and Visual Studio (`ProjectReference`); the SDK adds no scheduler of its own. The `Build` target's inputs include the sources of every `Source`-referenced project, so changing a library's source rebuilds whoever uses it; an unchanged graph runs `odin` zero times.

In Visual Studio: *Project > Add Project Reference...* (or the Add Reference command on the **Dependencies** node) opens the Reference Manager's Projects tab; the new `ProjectReference` is written to the `.odinproj` when the project is saved or built. It is a plain reference (a `Source` reference for an Odin project); to link an Odin DLL, add `ReferenceKind="Link"` by hand (see below). Visual Studio rejects a reference that would close a loop (*"Adding this project as a reference would cause a circular dependency"*). On the command line a loop fails the build with `ODIN0102`, naming the loop (`A -> B -> A`).

Two referenced projects must not use the same collection name with different roots (`ODIN0103`): give each a different `OdinCollectionName`, or remove the one that makes them match.

**IntelliSense across projects:** the language server (ols) is given one collection per `.odinproj` in the solution, referenced or not, so `import "name:pkg"` resolves in the editor for every project. ols takes one collection set per process, so two projects with the same name collide (info bar `ODIN0103`; the first project by path keeps the name). The names and roots are the values MSBuild evaluates, so imports, conditions and `$(...)` are honoured. ols restarts when a project is added, removed, loaded or unloaded, or when a project's collection name or root changes (saving the `.odinproj` is enough).

**Linking an Odin DLL (checked with real odin, command line):** give the library project `<OdinOutputType>Dll</OdinOutputType>` and reference it from the executable with `ReferenceKind="Link"`:

```xml
<ProjectReference Include="..\MyDll\MyDll.odinproj" ReferenceKind="Link" />
```

The DLL is built first, its import library goes to the linker (with its folder as `/LIBPATH`), and the `.dll` is copied next to your exe. In the Odin source, declare the procedures in a `foreign` block (`@(export)` and `"c"` on the DLL side). No `foreign import` is needed: the library is already on the link line (a `foreign import "system:MyDll.lib"` also works). Calling a procedure with no declaration is an error (`Undeclared name`), and importing the DLL project's package as a `Source` reference instead compiles its code into your exe, not the DLL.

```odin
foreign { dll_mul :: proc "c" (a, b: i32) -> i32 --- }
```

A `.vcxproj` reference is a Link reference too: the DLL project's import library is linked and its DLL (and PDB) copied next to the exe; `tests/samples/MixedSample.sln` shows an Odin app, a C++ DLL and a C# console in one solution (build it with Visual Studio's MSBuild, `dotnet msbuild` cannot build a `.vcxproj`). Samples: `tests/samples/LinkApp`, `LinkDll` (and `LinkLib`, the static one) in `OdinLinking.sln`. A project folder with spaces is fine (the library is passed relative to the project), but the library path itself cannot have spaces after that (`ODIN0107`). The DLL (and its PDB) is copied next to the exe on every build, also when the exe itself is up to date, so a deleted copy comes back. `Clean` removes them again, using the list the build keeps in `obj\<Configuration>\<project>.FileListAbsolute.txt` (never in the output folder); a copy left by an earlier set of references is not in the list, so run Clean after changing references.

**Known gaps:** `OdinOutputType` `Lib` (static library) fails with the installed Odin; see [Known limitations](limitations.md) for the rest.
