# Odin Language Support for Visual Studio 2026 — Project Brief

Personal-use Visual Studio 2026 extension adding Odin support: syntax highlighting, LSP diagnostics/IntelliSense via `ols`, an `.odinproj` project type in solutions, MSBuild-driven builds, and debugging.

## Decisions (already made)

| Area | Decision |
|---|---|
| Audience | Personal use only. Side-loaded `.vsix`; no Marketplace constraints. |
| Extension model | **Hybrid**: VisualStudio.Extensibility (new model) for the language server; VSSDK for the grammar, project system, and anything the new model doesn't cover. |
| Language server | `ols` (https://github.com/DanielGavin/ols), **bundled inside the VSIX** (no path setting). Launched like the Rust sample's `rust-analyzer.exe`. |
| Diagnostics | Parse/semantic errors (missing braces, type mismatches, unknown symbols) come **only from ols via LSP**. The grammar does not do this. |
| Highlighting | TextMate grammar registered via `.pkgdef` (VSSDK). Odin has no built-in VS grammar (unlike Rust). |
| Project model | **Solution baseline**: `.sln`/`.slnx` containing `.odinproj` projects, modeled on ZigVS's `.zigproj`. MSBuild reads `.odinproj` and invokes `odin.exe`. |
| Debugging | **Required.** VS Windows native debug engine on PDBs from `odin build ... -debug`, launched as the project's F5 target. |

## Hybrid model: how the projects split

`VSSDK+VisualStudio.Extensibility` is one VSIX project that hosts both models **in-process**. Microsoft's template for this is "VisualStudio.Extensibility Extension with VSSDK Compatibility". This is the documented hybrid; it is not "one model only".

Separate projects are used for separation of concerns (project system, build logic, templates), not because each model needs its own VSIX.

Alternative (not chosen): two VSIXes, an out-of-proc new-model LSP extension plus a VSSDK extension. That gives true out-of-proc isolation for the LSP but means two installs, and the two can't share code easily.

## Solution layout

```
OdinVS.sln
├─ src/OdinVS/                         # VSIX host (hybrid: VSSDK + VisualStudio.Extensibility)
│  ├─ OdinVS.csproj
│  ├─ source.extension.vsixmanifest    # ExtensionType="VSSDK+VisualStudio.Extensibility"; InstallationTarget [17.0,)
│  ├─ ExtensionEntrypoint.cs           # new-model entry point
│  ├─ OdinPackage.cs                   # VSSDK AsyncPackage (registers project factory, options)
│  ├─ odin.pkgdef                      # TextMate repo + project type registration
│  ├─ Server/ols.exe                   # bundled; Content, IncludeInVSIX=true
│  ├─ Grammars/odin.tmLanguage.json    # Content, IncludeInVSIX=true
│  └─ Grammars/language-configuration.json   # optional
├─ src/OdinVS.LanguageServer/          # new model: LanguageServerProvider for ols
├─ src/OdinVS.ProjectSystem/           # VSSDK: .odinproj project factory/hierarchy (ZigVS uses MPFProj)
├─ src/OdinVS.Build/                   # MSBuild targets/props (+ optional custom Task) for odin.exe
│  ├─ Odin.props
│  └─ Odin.targets                     # Build/Clean/Rebuild -> odin build; emits PDB on Debug
├─ templates/OdinApp/                  # project template (.odinproj + main.odin)
└─ samples/HelloOdin/                  # manual test solution
```

`OdinVS.LanguageServer` and `OdinVS.ProjectSystem` are class libraries referenced by the VSIX host. **Verify early** that new-model contributions (`[VisualStudioContribution]`) are discovered from a referenced assembly. If they aren't, move the provider into `src/OdinVS/`.

## `.odinproj` (initial shape)

```xml
<Project>
  <Import Project="$(OdinTargetsPath)\Odin.props" />
  <PropertyGroup>
    <OdinPackageDir>src</OdinPackageDir>          <!-- odin builds a directory -->
    <OutputType>Exe</OutputType>                 <!-- Exe | DynamicLibrary | StaticLibrary -->
    <OdinOptimization>none</OdinOptimization>
    <OdinExtraArgs></OdinExtraArgs>
  </PropertyGroup>
  <PropertyGroup Condition="'$(Configuration)'=='Debug'">
    <OdinDebug>true</OdinDebug>                  <!-- -debug => PDB -->
  </PropertyGroup>
  <ItemGroup>
    <OdinCompile Include="src\**\*.odin" />      <!-- display + incremental inputs only -->
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Lib\Lib.odinproj" />  <!-- ordering; maps to -collection or package path -->
  </ItemGroup>
  <Import Project="$(OdinTargetsPath)\Odin.targets" />
</Project>
```

Design notes:
- Odin compiles a directory, not a file list. `OdinCompile` items exist for Solution Explorer and incremental-build inputs; the compiler still gets `OdinPackageDir`.
- Configurations (Debug/Release) and platforms (x64) come from MSBuild properties and the VS Configuration Manager.
- `ProjectReference` gives build ordering across many projects. How a reference maps to Odin imports (collections vs. relative paths) is an **open design question**.
- Map the compiler's error output to MSBuild's canonical error format so build errors reach the Error List.

## Language server (modeled on the Rust sample)
- `DocumentTypeConfiguration("odin")` with `FileExtensions = [".odin"]` and `BaseDocumentType = LanguageServerBaseDocumentType`.
- `CreateServerConnectionAsync`: start `Server/ols.exe` with stdin/stdout redirected and return a `DuplexPipe`.
- `OnServerInitializationResultAsync`: on failure, log the error and set `Enabled = false`.

## Grammar registration (`odin.pkgdef`)
```
[$RootKey$\TextMate\Repositories]
"Odin"="$PackageFolder$\Grammars"
```

## Debugging
- The project system supplies the debug launch: the native engine on `$(TargetPath)`, working directory, and arguments from project properties.
- Optional later: a Natvis file for Odin types (slices, maps, `any`, strings).

## Reference implementations
- ZigVS (closest analog: `.zigproj`, MSBuild, MPFProj, native debugging): https://github.com/luckystar-studio/ZigVS
- Rust LSP sample (new model): https://github.com/microsoft/VSExtensibility/tree/main/New_Extensibility_Model/Samples/RustLanguageServerProvider
- TextMate in a VSIX (VSSDK): https://github.com/madskristensen/TextmateSample
- Hybrid model docs: https://learn.microsoft.com/visualstudio/extensibility/visualstudio.extensibility/get-started/in-proc-extensions
- LSP + TextMate docs: https://learn.microsoft.com/en-us/visualstudio/extensibility/adding-an-lsp-extension?view=visualstudio

## Known risks / open questions
1. **Project system cost.** This is the largest piece. MPFProj (legacy, used by ZigVS) vs. CPS (Common Project System, modern but heavy) is undecided. Start by studying ZigVS's MPFProj usage.
2. **VS LSP client compatibility with ols.** ZigVS needed special ZLS builds and inlay-hint workarounds. Test ols early.
3. **Version pinning.** Bundled `ols.exe` must match the installed Odin compiler. Record the pinned versions.
4. **ols config.** Does it need `ols.json` / collection paths? Ideally generate them from `.odinproj` references.
5. **Contribution discovery across assemblies** (see Solution layout).
6. **Grammar compatibility.** Some TextMate grammars don't work in VS. Validate the chosen grammar and its license.
7. **Semantic tokens.** Unconfirmed whether VS's LSP client consumes them. Assume colorization is grammar-only.
8. **Breakpoints in `.odin` files.** Verify they bind and that Watch/Locals are usable.

## Milestones
1. VSIX colorizes `.odin` files (grammar + pkgdef) in the experimental instance.
2. ols launches; diagnostics appear as squiggles and in the Error List.
3. `Odin.targets` builds an `.odinproj` from the command line (`msbuild Hello.odinproj`).
4. The project type loads in a solution; Build/Clean/Rebuild and the Configuration Manager work.
5. F5 debugging with working breakpoints.
6. Multi-project solution with `ProjectReference` ordering.

## Out of scope (for now)
Open Folder mode, Marketplace publishing, toolchain installer, Test Explorer, profiler integration, snippets, custom Fonts & Colors classifier.
