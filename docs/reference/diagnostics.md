# Diagnostics

Build output uses MSBuild's canonical format, so Visual Studio shows entries in the Error List and double-click navigates to the line:

```
src\main.odin(6,11,6,17): error ODIN0001: Cannot convert '"text"' to 'int' ...
```

Odin does not give its errors codes, so odin-vs assigns them.

| Code | Severity | When | Notes |
|---|---|---|---|
| `ODIN0001` | error | Odin reported a compile error with a position | One code for every compile error; the message is Odin's own |
| `ODIN0002` | warning | Odin reported a compile warning, for example use of a `@(deprecated)` procedure | Does not fail the build; the exe is still produced |
| `ODIN0100` | error | `odin build` failed (non-zero exit) but gave no error list, such as a link failure or crash | The message carries whatever Odin printed |
| `ODIN0101` | error | `odin.exe` was not found | Install Odin, add it to `PATH`, or set `OdinExe` |
| `ODIN0102` | error | A circular project reference, reported with its path (`A -> B -> A`) | Raised by the reference resolver when building from the command line (Visual Studio's own build has not been tried with a loop). Visual Studio's Add Reference refuses a loop before it is written, with its own message that has no path |
| `ODIN0103` | error (build); warning (info bar, log) | Two referenced projects use the same collection name with different roots. In the editor it is raised for any two projects in the solution, referenced or not, because the language server gets every project's collection | Give each project a different `OdinCollectionName`, or remove the one that makes them match |
| `ODIN0104` | error | A referenced project could not be read | Check that the file exists and loads |
| `ODIN0105` | error | A `Link` reference to a project that produces no library | Use a `Source` reference, or reference a library project |
| `ODIN0106` | error | `OdinOutputType` is not `Exe`, `Dll` or `Lib` | Fix the value in the `.odinproj` |
| `ODIN0107` | error | A library to link has a path with spaces that cannot be written relative to the project folder | Odin splits `-extra-linker-flags` on spaces; move the project or set `OutDir` so the library path has none |
| `ODIN0108` | error | Two linked libraries (Link references, including through Source references) have the same file name in different folders; the linker finds a library by name and would use only one | Give one of the projects a different `TargetName` |
| `ODIN0109` | error | The MSBuild host cannot build referenced projects from a task (no `IBuildEngine3`), so the reference graph cannot be read | Build with a current MSBuild (Visual Studio 2026 or the .NET SDK); not seen with either |
| `ODIN0110` | error | A collection name or root, or a library path to link, is empty or contains a double quote or control character (the language server is also not given such a collection) | Fix `OdinCollectionName` / `OdinCollectionRoot` (or the `Path` of an `OdinCollection` item), or the `OdinLinkLibrary` / `TargetName` of the referenced project |
| `ODINPKG1` | warning | Packing `Odin.Sdk` with `scripts\pack-sdk.ps1` | Reminder that the package is local only and must not be published; not a build diagnostic |
| `ols.<code>` | error | A diagnostic from the ols language server while editing (for example `ols.checker`, `ols.Syntax`) | Appears on save; not part of the build |

Notes:
- A syntax error stops Odin before semantic checking, so a build shows only syntax errors until they are fixed.
- In the editor, ols diagnostics with warning severity (such as a call to a `@(deprecated)` procedure) draw the editor's warning squiggle, which Visual Studio colours green by default; errors are red.
- Column numbers are 1-based; Odin's column 0 at end of file is shown as 1.

**Keep this table current.** Adding, renaming or removing any error, warning or other diagnostic code means updating it (and the constants in `src/OdinVS.Core/OdinDiagnosticCodes.cs` or the ols code mapper).
