# Pitfalls and verified behaviour

Each entry says what happened, whether it was confirmed in Visual Studio 2026, and what to do about it.

## Packaging

**No `.vsix` after a successful build.** A plain `dotnet build`, and an MSBuild build before the Build package was referenced, both produced only the DLL. Fix: add `Microsoft.VisualStudio.Extensibility.Build` and build with Visual Studio's MSBuild. *Confirmed.*

**Generate GUIDs, do not type them.** The VSIX identity, package, UI-context and project-type GUIDs are global, so create them with `[guid]::NewGuid()`. Patterned values (`A1111111-0000-4000-8000-000000000004`) are fine only for project IDs inside one `.sln`, where they need to be unique within that file. *Practice, not a VS finding.*

**API names drift between the docs and the package.** The shortcut type was not `ShortcutCarrier`, and `GetOrCreateEditor` was inaccessible. The compiler reported the real signatures. Fix: let the compiler report mismatches and check the signatures in the docs or XML for the pinned version. Reflection against the DLLs failed because their dependencies did not load, so it was not a reliable way to read the API. *Confirmed.*

## Shortcuts

**`Ctrl+Alt` cannot be bound directly.** See the modifier list in `SKILL.md`. The workaround is a chord. *Confirmed.*

**Shortcuts fire only in the editor.** Test them with focus in a text view. *Confirmed.*

## Editing

**Replace on an empty selection inserts at the caret.** This is the behaviour the command relies on. *Confirmed.*

**A one-character selection is replaced, not kept.** A click that left a single character selected (the `D` in `ToString("D")`) was replaced by the GUID. That is the intended behaviour, but it is easy to hit by accident during testing, so put the caret on an empty line before testing the caret path. *Observed.*

**Undo an accidental test edit.** When keyboard input is unavailable, use **Edit > Undo**. The tab should lose its unsaved-changes marker. *Confirmed.*

## Installing and testing

**The experimental hive.** `VSIXInstaller.exe /rootSuffix:Exp /a` exited with code 0, yet the extension was not present in the Exp hive on the first attempt. Verify the install by checking the instance rather than the exit code. *Observed, cause not found.*

**F5 is the reliable path.** Setting the extension as the startup project and pressing F5 is the documented route to the experimental instance. *Confirmed.*

**Scripted install to Exp: install, then merge, then launch.** For a pkgdef-driven VSIX (grammar, project type) the working sequence was `VSIXInstaller.exe /quiet /rootSuffix:Exp <vsix>`, then `devenv.exe /rootSuffix Exp /updateconfiguration`, then launch `devenv.exe /rootSuffix Exp <file>`. Without the merge the new pkgdef was not applied: the grammar stayed unregistered, the file was uncolored on every launch, and no grammar cache was written. After the merge it colored on the first launch. A reinstall of the same version does nothing, so bump `Identity Version` in the manifest each time. *Confirmed (0.1.3 and 0.1.4 on VS 18.11); `/updateconfiguration` was not run before the earlier 0.1.2 runs.*

**`&` does not wait for `VSIXInstaller.exe` or `devenv.exe`; use `Start-Process -Wait -PassThru`.** Both are GUI-subsystem programs, so `& exe args` returns at once, which is why `$LASTEXITCODE` stays empty and why launching Exp straight after an install raced it: the first Exp launch left the extension unloaded and only a second launch loaded it. With `Start-Process -Wait -PassThru` (read `.ExitCode`) a single launch loaded the extension (observed twice on VS 18.11). The race was suggested by a review and fits the evidence; not proven in isolation. Also ask Exp to close with `CloseMainWindow()` and force-kill only after a timeout, so unsaved edits to a sample are not lost. *Observed.*

**`VSIXInstaller` prints nothing and `$LASTEXITCODE` stays empty from PowerShell** (see the entry above for the cause). Read the newest `%TEMP%\dd_VSIXInstaller_*.log`; success ends with "Install to Visual Studio Community 2026 completed successfully" and names the new random folder under `%LOCALAPPDATA%\Microsoft\VisualStudio\18.0_<id>Exp\Extensions\`. The previous version's folder is removed. *Confirmed.*

**Only one Exp instance can be driven at a time, and a running one does not pick up a reinstall.** A second `devenv /rootSuffix Exp` attaches to the running instance. The installer still reports success while an Exp instance is running, but the running instance keeps the old state, so close it (only if you launched it; it may hold someone's unsaved work) before testing a new build. *Observed.*

**After every reinstall, delete the grammar cache, then merge, then launch.** `%LOCALAPPDATA%\Microsoft\VisualStudio\18.0_<id>Exp\TextMateCache\<Name>.language.cache` is XML naming the extension folder (`_repository`) and its grammars. `VSIXInstaller` rewrites it at install time pointing at the new random folder with an empty `<Grammars>` list, and VS does not rebuild it afterwards. Symptoms of a stale cache: no colors, and no TextMate editor features at all (auto-indent and comment toggling vanish too). Working sequence: install, delete that one file, `devenv /rootSuffix Exp /updateconfiguration`, launch (verified for 0.1.4 and 0.1.6). Merge alone with the stale file present did not work (0.1.5, 0.1.6). Earlier notes here that said deleting does not help were wrong: the delete only looked useless because the merge had not run yet. Any test run on a stale cache is invalid, so check the file's `_repository` matches the newest extension folder before trusting a result. *Confirmed.*

**Judge colors from a zoomed screenshot, not the first full-size one.** A dark theme at reduced scale makes colored and uncolored text look alike; zoom on a few lines. *Observed.*

**An automation with only "click" access to VS cannot type, so keyboard behaviour (auto-close, surround, word selection) needs the user.** Do not simulate it by other means. *Observed.*

## Uninstalling

**`VSIXInstaller.exe /u:"<Identity Id>"` did not remove the extension.** It opened the Visual Studio Installer, and the extension stayed in the menu after a restart, from both PowerShell and Command Prompt. The ID matched the manifest. Use **Manage Extensions > Installed > Uninstall** instead. *Confirmed on the test machine; root cause not found. The Visual Studio activity log (`%LOCALAPPDATA%\Microsoft\VisualStudio\18.0_*\ActivityLog.xml`) is the next place to look.*

**In the Experimental instance `/uninstall` works, and scripts can use it.** `VSIXInstaller.exe /quiet /rootSuffix:Exp /uninstall:<Identity Id>` (run with `Start-Process -Wait`; the installer is a GUI-subsystem program) removed the extension folder from the Exp hive, exit code 0; exit code 1002 means it was not installed. The failure above was seen on the main instance. VSIXInstaller's own log is `%TEMP%\dd_VSIXInstaller_*.log`: for a failed install read its `Exception` lines ("does not apply to any product" = broken manifest XML; "not a valid VSIX package" = a hand-built zip missing `manifest.json`/`catalog.json`). *Confirmed in Exp 18.11.*

## Launching and logging

**`devenv /log` takes a file name, not a flag.** `devenv /rootSuffix Exp /log "<solution>.sln"` made VS treat the solution path as the log file and overwrite it with the activity log. Always pass an explicit log path first: `devenv /rootSuffix Exp /log "<scratch>\activitylog.xml" "<solution>.sln"`. The log is UTF-16 XML and is written during the session. *Confirmed (and the solution was restored from git).*

**A pkgdef-only project type with a new GUID is "Unsupported".** Registering `.odinproj` under a new project type GUID with the CPS package GUID produced "Review Project And Solution Changes: Unsupported" when opened, although the pkgdef was loaded. VS's built-in `.msbuildproj` GUID is recognized. See `references/language-and-project-systems.md`. *Observed; cause not confirmed.*

**A CPS project shown as "(unloaded)" gives its reason only through Reload Project.** Select the node, Project > Reload Project: the dialog and Output window then show the real error (here: "missing some critical imports or the referenced SDK cannot be found"), while the activity log stays silent. Plain `Microsoft.Common.props/targets` imports did not satisfy that check. *Observed.*

## Building and NuGet

**Git Bash rewrites `/restore` into a path.** Running `MSBuild.exe proj /restore` from Git Bash fails with `Switch: C:/Program Files/Git/restore`. Run MSBuild from PowerShell, or use `-restore`. *Confirmed.*

**Offline restore works from the local cache.** `Microsoft.VSSDK.BuildTools` restored with no network because it was already in `%USERPROFILE%\.nuget\packages`; the output said "All projects are up-to-date for restore". Do not add or upgrade packages without asking; check the cache first. *Confirmed.*

**A content-only VSIX (grammar, templates, pkgdef) needs four switches.** `VSSDKBuildToolsAutoSetup=true` (else no `.vsix`), `CopyLocalLockFileAssemblies=false`, `GeneratePkgDefFile=false` (you ship your own `.pkgdef`) and no assembly asset in the manifest. Declare each shipped file as `Content` with `IncludeInVSIX=true` and a `VSIXSubPath`. *Confirmed.*

**Public CPS and VS SDK NuGet packages lag the installed VS.** The CPS packages stop at 17.9.x and the VS SDK at the 17.14 line while VS is 18.11; compile against the VS-installed assemblies instead (`HintPath`, `Private=false`). *Sourced from the spike notes.*

## Grammars and language configuration (TextMate)

See `references/language-and-project-systems.md` ("TextMate grammars in VS") for the registration recipe. Pitfalls:

**A grammar with no `fileTypes` is never applied to any file.** VS maps extensions only from the grammar's own `fileTypes` (the VS Code `package.json` mapping is ignored). Grammars taken from VS Code extensions usually lack it; add it. *Confirmed by decompiling `JsonGrammarLoader` and by the cache.*

**Comments in a `.tmLanguage.json` are accepted.** VS parses it with `JsonConvert.DeserializeObject` (Newtonsoft), which skips `//` and `/* */`. This lets a vendored grammar mark local changes with `// MYPROJ(START)` / `// MYPROJ(END)` tags so `grep` lists every divergence. Verified by parsing with VS's own `Newtonsoft.Json.dll` and by colors in Exp with the tags present. Other tools (VS Code, `jq`) may reject the file. *Confirmed.*

**Pin and hash a vendored grammar, and stop git rewriting it.** With `core.autocrlf=true`, add `<path> -text` to `.gitattributes`, or line endings change and the recorded SHA-256 stops matching. *Confirmed.*

**TextMate auto-close and surround did not work because VS had brace completion switched off for the view (fixed, VS 18.11 Exp).** Typing `( { [ " '` closed nothing in a custom TextMate language or in VS's own `.bicep`, and surround-with did not wrap, while C# worked in the same instance; config tweaks (`autoCloseBefore`, `ContentTypeMapping`, removing `autoClosingPairs`) changed nothing and the activity log was silent. An earlier version of this note called that a platform limitation and said not to spend more time; that was wrong. Finding the cause took one log line: a command handler (any `IChainedCommandHandler<TypeCharCommandArgs>` exported for the content type) logged `IBraceCompletionManagerFactory.TryGetBraceCompletionManager(view)` and `view.Options.GetOptionValue(DefaultTextViewOptions.BraceCompletionEnabledOptionId)` when `(` was typed. Result: manager present, `Enabled=False`, option `False`. VS (`SimpleTextViewWindow`, decompiled) sets the `BraceCompletion/Enabled` option on each editor view from the language's text-manager preferences (`fBraceCompletion`); a language with no registered language service, which is every TextMate-only language, comes out false, and `BraceCompletionManager.PreTypeChar` returns at once when it is off. **Fix:** an `IWpfTextViewCreationListener` for the content type (`[ContentType]`, `[TextViewRole(PredefinedTextViewRoles.Editable)]`) that sets `view.Options.SetOptionValue(DefaultTextViewOptions.BraceCompletionEnabledOptionId, true)` when the view is created and again from `view.Options.OptionChanged` whenever VS switches it back off (compare `e.OptionId` with the key's `.Name`). After that auto-close, overtype of the closing character and surround-with (select text, type `(`) all worked. **Not isolated:** the same change also added an MEF `[Export(typeof(IBraceCompletionDefaultProvider))] [ContentType(...)]` with `[BracePair('(', ')')]`-style attributes (all six pairs); whether the TextMate `autoClosingPairs` alone would have worked once the option was on was not tested separately, so ship the provider as the known-good setup. *Confirmed by typing in Exp.*

**The method that found it: log the state a feature depends on, from code that runs on the same event.** Decompiling showed the three gates (manager exists, manager `Enabled`, a provider for the content type and character); one log line showed which gate was closed. Decompile first (`ilspycmd -p -o <scratch> Microsoft.VisualStudio.Text.UI.dll`, `Microsoft.VisualStudio.Platform.VSEditor.dll` and `Microsoft.VisualStudio.Editor.Implementation.dll` under `Common7\IDE\CommonExtensions\Microsoft\Editor\`; `Editor.Implementation` throws a few decompile errors and writes long paths, so search with Grep, not PowerShell `Select-String -Recurse`).

**A `wordPattern` that treats a number as a word makes VS open a completion list inside the literal.** With `(-?\d*\.\d\w*)|(...)` typing `f` after `1.5` made `1.5f` one word, a session started and the language server answered with identifiers. The pattern itself is not the bug (completion spans come from the server's `textEdit`); the session start is. Fix without touching the pattern: a command handler `[Export(typeof(ICommandHandler))] [Name(...)] [ContentType(...)] [TextViewRole(PredefinedTextViewRoles.Editable)] [Order(Before = "CompletionCommandHandler")]` implementing `IChainedCommandHandler<TypeCharCommandArgs>` that, when the typed character continues a number token, inserts it with `IEditorOperationsFactoryService.GetEditorOperations(view).InsertText(...)` and does not call the next handler; otherwise it calls `nextCommandHandler()`. VS's completion handler is named `CompletionCommandHandler` (decompiled). *Confirmed in Exp: no list opens, identifiers and Ctrl+Space unaffected.*

**Tab accepts the top-ranked completion item, not a specific one.** `fmt.pri` + Tab gave `print` once and `println` another time; both are real procedures. Write expectations as "accepts the top item", not as a particular name. *Observed.*
**Without `indentationRules`, Enter inherits the line above's indent; adding Bicep-style rules did not change a custom language's behaviour in one valid run.** Cause not found. *Observed.*

**Related public report.** Developer Community post 11077179 ("Language Configuration / TextMate grammar packaging from VSIX does not work in Visual Studio 2026", Apr 2026, VS 18.5, status Under Consideration, no engineering reply) describes a VSIX-packaged grammar not coloring. It does not cover brackets or indent. Its setup used a `.plist` grammar and no `GrammarMapping`, and it does not say whether the grammar cache was deleted or `/updateconfiguration` run, which together produced the same symptom here. *Read, cause for that post unknown.*

**Controls beat code reading.** Decompiled code said auto-close should work; only a control language (Bicep) and a control editor (C#) located the problem. Run a control early.

**`folding` appears in the config contract but gave no outlining.** Edit > Outlining offered only the generic toggles for a TextMate-only language, so proc-level "Collapse to Definitions" needs another source (for example LSP `foldingRange`, unchecked). *Observed negative result.*

**Light and Dark themes color the same scopes differently.** Check both; a scope that looks right in one can be near-invisible in the other. *Observed.*

## Language clients (classic `ILanguageClient`, stdio server)

All *Confirmed* in VS 18.11 Exp with a stdio language server unless noted.

**Wiring.** A public `[ContentType("x")][Export(typeof(ILanguageClient))]` class, a content type whose base is `CodeRemoteContentDefinition.CodeRemoteContentTypeName`, and a `[FileExtension]` mapping. When the MEF types live in a referenced project, the vsixmanifest needs `<Asset Type="Microsoft.VisualStudio.MefComponent" Path="|ThatProject|" />`. Reference `Microsoft.VisualStudio.SDK` and `Microsoft.VisualStudio.LanguageServer.Client` (`ExcludeAssets="runtime"`), add `<Reference Include="System.ComponentModel.Composition" />`, and set `CopyLocalLockFileAssemblies=false`. The client only activates when a document of that content type opens, so a log line in `OnLoadedAsync` shows whether it ran at all.

**Server settings.** Pass them through `InitializationOptions` (a `JToken`). A config file the server reads from the workspace root is not found for a loose file opened outside a solution.

**The child server can outlive VS.** An orphaned server stayed running after Exp closed. Put it in a Windows job object with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` (P/Invoke `CreateJobObject`, `SetInformationJobObject` class 9, `AssignProcessToJobObject`) held for the VS process lifetime; no server remained afterwards. Handle the `Exited` event defensively (read `ExitCode` inside try/catch): it runs on a threadpool thread and can race `Dispose`.

**Middle layer: use `ILanguageClientMiddleLayer2<JToken>`** (the non-generic one is obsolete). If your handler throws before calling `sendNotification`, VS silently receives nothing: no squiggles, an empty Error List, while the server log shows it publishing. Wrap the reshaping in a catch-all that logs and forwards the original.

**What the Error List shows for pushed `publishDiagnostics`.** `code` fills the Code column and prefixes the squiggle tooltip (rewrite it there). The Source column read "IntelliSense" whatever `source` was, so setting `source` does nothing visible. `_vs_projects` (the Project column) is only converted for VS's `VSDiagnostic` objects, built on the pull path (`textDocument/diagnostic`), so a push-only server leaves Project empty (decompiled; the empty column was observed with the data supplied).

**Activation failures (decompiled, crash restart observed).** Returning null from `ActivateAsync` raises VS's failure notification; on a disconnect VS retries activation up to five times (a killed server restarted in about 0.2 s).

**Completion and hover.** A completion item's `textEdit` range wins as the replaced span; the TextMate `wordPattern` is only the fallback (decompiled). Hover markdown is rendered by VS; code fences get a Copy code button and there is no per-language hover style setting. *Observed.*

**`file:///D%3A/...` URIs break `Uri.LocalPath`.** Servers send the drive colon percent-encoded; on .NET Framework `LocalPath` gives `/D:/...` and file APIs throw `NotSupportedException`. Decode `Uri.UnescapeDataString(uri.AbsolutePath)`, drop the leading `/` before a drive letter, swap `/` for `\`, and treat a non-empty `Host` as a UNC path.

**VS threading analyzers fail the build under `TreatWarningsAsErrors`.** Blocking on a task (`.Result`, `.Wait()`) raises `VSTHRD002`; read a child's output with `BeginOutputReadLine` and the event instead, then `WaitForExit()` to flush it.

**Decompiled VS assemblies are the reference.** `ilspycmd` on `Microsoft.VisualStudio.LanguageServer.Client*.dll` answered how diagnostics, completion spans and activation work when the docs did not.

## Windows PowerShell and BOMs

**Windows PowerShell 5.1 adds a UTF-8 BOM in several common cases**, and a BOM is invisible in most tools: `Set-Content -Encoding utf8`, `Add-Content -Encoding utf8`, `Out-File -Encoding utf8` and `>`. Plain `Set-Content` without `-Encoding` writes the ANSI code page instead. Confirmed today: a version bump done with `Set-Content -Encoding utf8` put a BOM on `source.extension.vsixmanifest` (found with `head -c 3 | od -c`). A git commit message made from a PowerShell here-string once began with a BOM too; the exact mechanism there was not proven. *Confirmed for `-Encoding utf8`; suspected for the here-string.*

What to do: edit files with the Edit/Write tools or Bash (`sed`), commit with `git commit -F <file>` from Bash, and when PowerShell must write a file use `[IO.File]::WriteAllText($path, $text, (New-Object Text.UTF8Encoding $false))` (or PowerShell 7, whose `utf8` has no BOM). Check with `head -c 3 <file> | od -c` (a BOM shows `357 273 277`) and `git log -1 --format=%B | od -c | head -1`. The Write tool also preserves an existing file's encoding, so overwriting a UTF-16 file keeps it UTF-16.

## Source control

**`bin/`, `obj/`, `.vs/`, and `.claude/settings.local.json` were initially untracked and would have been committed.** Add a `.gitignore` before the first commit. *Confirmed.*

**Git refused to read the repository after a folder ownership change.** Git reported "dubious ownership" because the folder belonged to another account's SID. Fix: take ownership of the folder, not a global `safe.directory` exception. *Confirmed.*

## Debugging: F5 providers, PDB types and natvis (VS 18.11)

**A CPS `[ExportDebugger]` provider is MEF, so its assembly needs a `Microsoft.VisualStudio.MefComponent` asset.** A package-only (`VsPackage`/`Assembly`) asset is not enough: add `<Asset Type="Microsoft.VisualStudio.MefComponent" Path="|YourProject|" />` for the project that holds the provider, next to its existing asset. With it, F5 on a custom CPS project launched, bound a breakpoint and stopped (`Process: <exe>`, `Stack Frame: main::main`). *Confirmed.*

**Read launch values from the evaluated project, not from hard-coded paths.** `ConfiguredProject.Services.ProjectPropertiesProvider.GetCommonProperties().GetEvaluatedPropertyValueAsync("TargetPath")` (and `LocalDebuggerWorkingDirectory`, `LocalDebuggerCommandArguments`) gave the right values; a `Debugger`-template rule that lists those two properties makes them editable on the Debug page. VS builds before `CanLaunchAsync` runs, so a missing exe there means the build failed or `TargetPath` is wrong. `DebugLaunchOptions` is `0` for F5 and carries `NoDebug` for Ctrl+F5, and the same provider serves both. *Confirmed (F5); Ctrl+F5 observed by the user (the program ran).*

**Match the C# project system for launch errors: `CanLaunchAsync` returns true, `QueryDebugTargetsAsync` throws a clear message.** Decompiled `ProjectLaunchTargetsProvider.ValidateSettings` (`Microsoft.VisualStudio.ProjectSystem.Managed.VS.dll` under `Common7\IDE\Extensions\Microsoft\ManagedProjectSystem\`) throws for a missing executable ("The debug executable '{0}' specified in the '{1}' debug profile does not exist.") and a missing working directory, and `LaunchProfilesDebugLaunchProvider.CanLaunchAsync` is always true. A custom provider doing the same showed VS's message box with the exception text in Exp 18.11; returning false from `CanLaunchAsync` instead makes F5 do nothing visible. *Confirmed for the working-directory case; the C# dialog itself was not compared.*

**Box and multi-caret typing is VS's own behaviour; do not try to fix it.** `AutoSurroundCommandHandler` (decompiled, `Microsoft.VisualStudio.Platform.VSEditor.dll`) passes through unless every selection is non-empty, so several empty carets never auto-close, and `BraceCompletionManager.TryAutoSurround` computes `new Span(start + 1, end - start - 2)` from the whole selection extent, which wraps a box selection badly and can throw `ArgumentOutOfRangeException`. The same wrapping was seen in a C# file in the same Exp instance. A custom typing handler should fall through (`GetMultiSelectionBroker().AllSelections.Count > 1`) rather than act on the primary caret only. *Confirmed by the user in Exp.*

**Extension MEF exports must be scoped by content type, and natvis is global.** Brace-completion providers, command handlers and view listeners with `[ContentType("<yours>")]` leave C#/C++ editing untouched. A `.natvis` shipped in a VSIX applies to every native debuggee, so a `Type Name` like `string` can also match another project's type of that name: **confirmed**, a C++ `struct string { char* data; long long len; }` showed as `"hello"` through a shipped `string` entry (with no other extension installed). `std::string` was unaffected. The fix that worked was to drop the global entry and show the type from a Concord component behind a language gate (see `concord-debugger.md`). Feeding natvis to one project's debug session only (as C++ projects do with a `Natvis` item) from a CPS launch provider is untested.
**The native engine debugs a non-C++ compiler's PDB with no extra support.** Breakpoints in the language's own source files bound and hit, call stack, Locals and Watch worked, and the Language column in Call Stack shows `C`. Composite types keep the front end's own names in the `Type` column; scalar types appear as the closest CodeView built-ins (`__int64`, `unsigned char`). *Confirmed with Odin.*

**Natvis can ship in a VSIX, and it loads only when the debugger starts.** `<Asset Type="NativeVisualizer" Path="Visualizers\x.natvis" />` (plus the file as `Content` with `IncludeInVSIX`) put the file in the extension folder, and the debugger parsed it from there on the next F5 (Output showed `Natvis: Parsing natvis xml file: ...\Extensions\<id>\Visualizers\x.natvis`). Reinstall needs a version bump as for any extension. *Confirmed.*

**Turn on natvis diagnostics before guessing.** Tools > Options > Debugging > Output Window > "Natvis diagnostic messages (C++ only)" = Verbose. The label says C++ but it logs for any native-engine debuggee. Each entry then logs "Successfully parsed expression ..." or `Error: Invalid type name '...'` to Output (Debug), with the file and line. Without it a visualizer that does not apply fails silently. *Confirmed.*

**A natvis `Type Name` must parse as a C++ type name.** `[]int`, `[]*`, `*]int`, `*[]int` and even a bare `*` were all rejected with `Invalid type name`, while an identifier (`string`) parsed. Matching is by type name only; natvis has no structural match, so types whose PDB names are not C++-shaped (for example a front end that names a slice `[]int`) cannot be visualized with natvis. Wildcards are documented for template arguments (`Foo<*>`). Alternatives need a debugger extension (Concord visualizer or evaluator), which was not built. *Confirmed by the diagnostics log; the reason is inferred from the messages.*

**Natvis `string` view of a pointer+length pair.** `<DisplayString>{data,[len]s8}</DisplayString>` with `<StringView>data,[len]s8</StringView>` showed the text (`"odin"`) and `ArrayItems` with `Size`/`ValuePointer` listed the characters. Applying a visualizer replaces the default `{data=... len=4}` value; the original members move under `[Raw View]`. *Confirmed.*

**Computer-use access to Visual Studio is click-only, and it can be enough.** The tier for IDEs allows clicks and screenshots but no typing or keys, and `request_access` for an IDE says to call it again in the SAME turn to confirm (the second call shows the approval dialog). With it, Start, Continue and Stop on the toolbar, the Locals/Watch tabs, expand arrows and the editor gutter (a click sets a breakpoint) were driven from screenshots, and `zoom` on the Locals region made text legible. Typing into Watch is not possible, so give the sample a self-breaking line (`__debugbreak()` in C/C++, a breakpoint set once in Odin) and put the values to inspect in Locals. Coordinates are fixed per layout (toolbar Start is at a stable x with no session, Continue/Stop differ once debugging starts); re-screenshot after each layout change, because expanding rows shifts everything below. Other times the user ran the checklist and sent screenshots, which still works. *Observed in Exp 18.11.*

**Windows Smart App Control can block freshly built unsigned binaries, which looks like a random build, test or Exp failure.** Symptoms seen: `dotnet test` failing with "An Application Control policy has blocked this file (0x800711C7)", a toolchain's `run` command failing with "Failed to execute command" on the exe it just built (one rare test failure), and Exp showing a package-load error for a fresh DLL. Event Viewer > Applications and Services Logs > Microsoft > Windows > CodeIntegrity > Operational has events 3077, 3033 and 3118 naming the blocked file. It is a system security setting: never turn it off or work around it from a session; stop, tell the user, and retry only when they say it is cleared. Keep the full output of the failing run before retrying. A test that builds and starts its own exe can retry a refused start, which only helps the intermittent case. *Observed 2026-11-10 (Windows 11, VS 18.11).*

**Breakpoints in an Exp sample are line-number bound: editing the sample above the breakpoint moves the code, not the breakpoint.** After adding lines to the debuggee's source, re-check where the breakpoint sits (it may stop earlier than the data you want to inspect). Click the gutter on the intended line instead of trusting the old one. *Observed.*

**Verify what was typed in Exp versus what only compiled.** Features reachable only by typing in Watch (rewrites, lookups) cannot be driven click-only, so list for the user exactly which expressions to type, record which were seen and which were only implemented, and put the unseen ones in the docs and the PR as such. *Learned over the odin-vs Watch work.*

## Restarting a classic language client (VS 18.11, *Verified* in VS Exp)

`ILanguageClient` can restart its server on demand: raise `StopAsync` and then `StartAsync` (the events you already implement); VS calls `ActivateAsync` again, so recompute `InitializationOptions` there. The old server process exits (exit code 0 in the log) and a new one starts. Debounce triggers that arrive in bursts (a solution that is still loading reports every project in turn): wait for a quiet period with a cap, or the server restarts several times while the solution opens. Skip the restart when no server is running; the next activation reads the current state anyway.

## Info bars that several sources update

Read the text that goes into the bar after switching to the UI thread, not before: two overlapping updates otherwise let a stale text win. `IVsInfoBarUIEvents.OnClosed` of a removed bar can arrive after a newer bar was shown, so the handler must clear the "current bar" field only when it still holds its own element (`ReferenceEquals`).

## Logs of a process started from the Claude desktop app

The Claude desktop app is a packaged (MSIX) app, and files that processes it starts write under `%LOCALAPPDATA%` can land in `%LOCALAPPDATA%\Packages\Claude_<id>\LocalCache\Local\...` instead (observed: an Experimental instance started by a script run from the app wrote its extension log there). If an expected log file is missing or stale, look under that package folder, or start the instance yourself. *Confirmed once; the redirect is Windows' packaged-app file virtualization, not something the extension does.*
