---
name: visual-studio-2026-extensions
description: Build, package, install, test, and uninstall Visual Studio 2026 extensions using the VisualStudio.Extensibility (out-of-process) model. Use this skill whenever the user mentions a Visual Studio extension, VSIX, Extensions menu command, editor command, keyboard shortcut for VS, VisualStudio.Extensibility, VSSDK, VSIXInstaller, the experimental instance, or wants to add a feature to Visual Studio 2026, even if they don't say "extension" explicitly. Also covers language support (TextMate grammars, LSP/language servers, ILanguageClient), custom project systems (MPFProj, CPS, project types, .sln integration), and MSBuild-driven build integration for VS extensions.
---

# Visual Studio 2026 extensions

This skill captures what worked when building a small Visual Studio 2026 extension (an Extensions menu command that inserts a GUID, with a keyboard shortcut). It covers the new `VisualStudio.Extensibility` model, the packages that must be referenced, how to build a `.vsix`, and how to install, test, and remove it.

Read `references/pitfalls.md` before touching the shortcut, the uninstall steps, or the classic VSSDK. It holds the verified problems and their workarounds, including the Exp install-then-`/updateconfiguration` sequence, the TextMate grammar cache, NuGet/MSBuild build pitfalls, the classic `ILanguageClient` language-server recipe and pitfalls (diagnostics display, middle layer, child-process lifetime) and the PowerShell BOM trap. Before scripting any file write from Windows PowerShell 5.1, read its "Windows PowerShell and BOMs" section.

Read `references/concord-debugger.md` before writing any debugger extension (Concord evaluators/visualizers, `.vsdconfig`, `DkmDataItem`). It holds the setup recipe and the threading/re-entrancy rules that caused Exp crashes and hangs.

Read `references/language-and-project-systems.md` before planning any extension that adds a **language** (grammar, language server), a **custom project type** (MPFProj vs CPS vs Open Folder), MSBuild-driven builds, or project references. It compares the options, lists the questions to ask the user first, and tags every claim as verified, sourced, or unverified.

## Choose the model first

| | `VisualStudio.Extensibility` (new) | Classic VSSDK |
| --- | --- | --- |
| Process | Out-of-process. The extension runs in its own host, so a crash doesn't take down Visual Studio. | In-process. The extension runs inside `devenv.exe`. |
| Project | SDK-style `net8.0` project, `IsVisualStudioExtension=true`, plus the packages below. | Legacy VSIX project with `.vsct` command tables. |
| Commands | C# classes with `[VisualStudioContribution]` and a `CommandConfiguration`. | `.vsct` XML plus a `Package` class. |
| Editor access | `this.Extensibility.Editor().EditAsync(...)` with text view snapshots. | Direct use of the editor and text manager interfaces. |
| Recommended for | New extensions that only need commands, editor edits, and similar features. | Features the new model doesn't yet expose, or existing extensions that already depend on it. |

Choose `VisualStudio.Extensibility` for new work unless a needed API is missing. Custom project systems and `ILanguageClient`-based language support need the classic VSSDK; a hybrid VSIX runs the new-model part in-proc, which loses its crash isolation (see the language and project systems reference). Confirm the API exists in the current SDK before relying on it, because the new model is still changing. This was checked against the 17.14 packages in this project. The classic VSSDK details above come from general knowledge and were not verified in this project.

## The packages

Reference these three in the `.csproj`. Pin them to the same version line (here `17.14.x`):

```xml
<PackageReference Include="Microsoft.VisualStudio.Extensibility.Build" Version="17.14.40608" PrivateAssets="all" />
<PackageReference Include="Microsoft.VisualStudio.Extensibility.Sdk" Version="17.14.40608" PrivateAssets="all" />
<PackageReference Include="Microsoft.VisualStudio.Extensibility" Version="17.14.2098" />
```

What each one does:

- **`Microsoft.VisualStudio.Extensibility`** is the runtime API you code against (`Command`, `Extension`, `IClientContext`, `ITextViewSnapshot`, and so on).
- **`Microsoft.VisualStudio.Extensibility.Sdk`** provides the source generators and attributes that turn `[VisualStudioContribution]` classes into registrations.
- **`Microsoft.VisualStudio.Extensibility.Build`** provides the MSBuild targets that package the output into a `.vsix`.

The Build package is not an alternative API to the VSSDK. It is the packaging step. Without it the build compiles a DLL but no `.vsix` is produced. The Sdk and Build packages are both needed.

Set `<IsVisualStudioExtension>true</IsVisualStudioExtension>` in the project.

## Minimal extension

Entry point (`InsertNewGuidExtension.cs`):

```csharp
[VisualStudioContribution]
internal class InsertNewGuidExtension : Extension
{
    public override ExtensionConfiguration ExtensionConfiguration => new()
    {
        Metadata = new(
            id: "InsertNewGuid.6f1c2a9e-3d4b-4c8a-9e7f-2b5d8a1c4e60",
            version: this.ExtensionAssemblyVersion,
            publisherName: "Local",
            displayName: "Insert New Guid",
            description: "Adds an Extensions menu command that inserts a new GUID at the caret."),
    };
}
```

Command (`InsertNewGuidCommand.cs`), inserting over the selection or at the caret:

```csharp
[VisualStudioContribution]
internal class InsertNewGuidCommand : Command
{
    public InsertNewGuidCommand(VisualStudioExtensibility extensibility) : base(extensibility) { }

    public override CommandConfiguration CommandConfiguration => new("%InsertNewGuid.DisplayName%")
    {
        Placements = new[] { CommandPlacement.KnownPlacements.ExtensionsMenu },
        Shortcuts = new[]
        {
            new CommandShortcutConfiguration(ModifierKey.Control, Key.K, ModifierKey.ControlShiftLeftAlt, Key.G),
        },
    };

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        string guid = Guid.NewGuid().ToString("D");

        using ITextViewSnapshot? textView = await context.GetActiveTextViewAsync(cancellationToken);
        if (textView is null)
        {
            return;
        }

        await this.Extensibility.Editor().EditAsync(
            batch =>
            {
                textView.Document.AsEditable(batch).Replace(textView.Selection.Extent, guid);
            },
            cancellationToken);
    }
}
```

Behaviour confirmed in Visual Studio 2026:

- `textView.Selection.Extent` replaces the selection when there is one. When the selection is empty it is the caret, so the GUID is inserted at the caret.
- The replacement also applies to a one-character selection. In testing, a click that left the single character `D` selected inside a string literal replaced that character, which is the intended behaviour for a selection.

Display strings go in `src/<Project>/.vsextension/string-resources.json`, referenced as `%Key%` (here `%InsertNewGuid.DisplayName%`).

## Shortcuts

The shortcut API accepts only these modifiers: `LeftAlt`, `Shift`, `Control`, `ControlShift`, `ControlShiftLeftAlt`, and `None`. A plain `Control+Alt` is not one of them, so `Ctrl+Alt+G` cannot be expressed. The closest working chord was `Ctrl+K, Ctrl+Shift+Alt+G`, which is a two-step chord (`Control+K`, then `ControlShiftLeftAlt+G`).

Shortcuts are active only in the editor context. They do nothing when focus is elsewhere.

Before you choose a shortcut, check **Tools > Options > Environment > Keyboard** for conflicts on the target machine. A binding that is free on one install can be taken on another.

## Build and package

Build with Visual Studio's own MSBuild, not `dotnet build`:

```bash
"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" InsertNewGuid.slnx /restore /p:Configuration=Debug
```

Output: `src\<Project>\bin\Debug\net8.0\<Project>.vsix`. Plain `dotnet build` compiles the DLL but skips packaging.

The package identity is the `Identity Id` in the generated `extension.vsixmanifest` (under `bin\...`). The `id:` in the extension class must match it, and the uninstall steps depend on it.

## Test in the experimental instance

1. Set the extension project as the startup project and press F5.
2. Visual Studio starts a separate experimental instance (`devenv /rootSuffix:Exp`). The main instance is not affected.
3. In the experimental instance, test the command in a text file. Cover: empty caret, selection, the menu item, and the shortcut.

To install the `.vsix` into the experimental hive without F5, run:

```bash
"C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\VSIXInstaller.exe" /rootSuffix:Exp /a "<path-to-vsix>"
```

The installer exited with code 0 but the extension was not registered in the Exp hive the first time, so verify the install in the instance itself rather than trusting the exit code. Launching the experimental instance once before installing may be needed for the hive to exist.

## Install into the main instance

1. Close all Visual Studio instances.
2. Double-click the `.vsix`, or run `VSIXInstaller.exe` with the `.vsix` path and no `/rootSuffix`.
3. Start Visual Studio and check **Extensions > Manage Extensions > Installed**. The extension should appear with the publisher you set.

## Uninstall

Use **Extensions > Manage Extensions > Installed**, select the extension, click **Uninstall**, then restart Visual Studio. This is the method that worked.

Do not rely on `VSIXInstaller.exe /u:"<Identity Id>"`. On the machine where it was tested it opened the Visual Studio Installer and left the extension installed, even after running from Command Prompt and restarting. The ID was correct, so the failure is in how the installer handles this install, and its cause was not found.

## Checklist for a new extension

- [ ] Three packages referenced, pinned to one version line, `IsVisualStudioExtension` set.
- [ ] Extension `id` matches the `Identity Id` in the built manifest.
- [ ] Strings live in `.vsextension/string-resources.json`.
- [ ] Shortcut uses only the supported modifiers, and was checked for conflicts.
- [ ] Built with Visual Studio's MSBuild, and a `.vsix` was produced.
- [ ] Tested in the experimental instance: empty caret, selection, menu, shortcut.
- [ ] Uninstall tested through Manage Extensions.
- [ ] `bin/`, `obj/`, `.vs/`, and `*.vsix` are excluded from source control.
