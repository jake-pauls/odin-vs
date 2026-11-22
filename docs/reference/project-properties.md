# Project properties and items

Everything an `.odinproj` can set, with its default. Properties are plain MSBuild properties: set them in the project's own `PropertyGroup` to override the default. Compiler options are deliberately not modelled as properties; pass any `odin` flag through `OdinExtraArgs`. **This list is kept current: it is updated whenever a project property or item is added or wrapped.**

| Property | Default | Meaning |
|---|---|---|
| `Configurations` | `Debug;Release` | Configurations the project declares (see [Configurations](configurations.md) for Visual Studio) |
| `Platforms` | `x64` | Platforms the project declares; only x64 is supported |
| `Configuration` | `Debug` | The configuration to build when none is passed (`/p:Configuration=...`) |
| `Platform` | `x64` | The platform to build |
| `OdinExe` | empty (search `PATH`, then `C:\Program Files\Odin\odin\dist\odin.exe`) | Path to `odin.exe` |
| `OdinPackageDir` | `src` | Directory passed to `odin build` (relative to the project, or absolute) |
| `OdinExtraArgs` | empty | Extra arguments appended to the `odin build` command: the place for any `odin` compiler option (`-o:speed`, `-define:X=1`, `-vet`) |
| `OdinDebug` | `true` in Debug, `false` otherwise | Adds `-debug` (and produces the `.pdb`). A default you can override, e.g. `<OdinDebug>true</OdinDebug>` for a Profile configuration |
| `OutDir` | `bin\<Configuration>\` | Output directory (relative to the project, or absolute) |
| `TargetName` | project name | Output file name without extension |
| `LocalDebuggerCommandArguments` | empty | Arguments passed to the program on F5 and Ctrl+F5 |
| `LocalDebuggerWorkingDirectory` | the project directory | Working directory on F5 and Ctrl+F5 (relative to the project, or absolute) |
| `TargetPath` | `<OutDir><TargetName>` + `.exe`, `.dll` or `.lib` by `OdinOutputType` | Full output path (relative to the project, or absolute); the `.pdb` is written next to it with the same name |
| `TargetExt` | `.exe`, `.dll` or `.lib` by `OdinOutputType` | Extension of the output file; normally left alone |
| `LocalDebuggerCommand`, `LocalDebuggerDebuggerType` | `$(TargetPath)`, `Auto` | Defaults Visual Studio itself reads. The Odin launch provider ignores them: F5 and Ctrl+F5 start `TargetPath`, so changing these does not change what runs |
| `DebuggerFlavor` | `OdinDebugger` | Selects the Odin launch provider; do not change |
| `OdinCollectionName` | project name | Name other projects use to import this one when they reference it: `import "<name>:<path>"` (see [References](../user-guide/references.md)) |
| `OdinCollectionRoot` | the project directory | Directory the collection name points at (relative to the project, or absolute) |
| `OdinOutputType` | `Exe` | What the project builds: `Exe`, `Dll` or `Lib` (static library). Selects `-build-mode:` and the output extension (`.exe`, `.dll` with its import `.lib` next to it, `.lib`). Any other value is an error (`ODIN0106`). **`Lib` does not work yet:** with the installed Odin (`dev-2026-10`) `-build-mode:lib` fails to link on Windows (see [References](../user-guide/references.md)). F5 on a `Dll` project says it builds a library and cannot be started (seen in Visual Studio; not tried for `Lib`, which does not build) |
| `OdinLinkLibrary` | the import `.lib` for `Dll`, the `.lib` for `Lib`, otherwise empty | The library file a project that links this one passes to the linker; empty for executables, so linking an executable is an error (`ODIN0105`) |

| Item | Purpose |
|---|---|
| `OdinCompile` | The project's own source files, e.g. `<OdinCompile Include="src\**\*.odin" />`. The SDK adds none; the `.odinproj` lists them. Changing one rebuilds |
| `OdinCollection` | An `-collection:name=path` passed to `odin`: `<OdinCollection Include="name"><Path>dir</Path></OdinCollection>` |
| `ProjectReference` | A reference to another project, `<ProjectReference Include="..\Lib\Lib.odinproj" />`; optional metadata `ReferenceKind` = `Source` or `Link` (see [References](../user-guide/references.md)) |
| `ProjectConfiguration` | Declares a configuration to Visual Studio; see [Configurations](configurations.md) |

Incremental builds look at the `OdinCompile` files and the `.odinproj`, so changing a property only with `/p:` on the command line does not trigger a rebuild; use the Rebuild target.
