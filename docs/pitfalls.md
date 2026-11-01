# Pitfalls

Verified problems and their workarounds. Add new ones with how they were verified.

## `Uri.LocalPath` breaks on ols file URIs
ols sends drive-letter URIs with the colon percent-encoded: `file:///D%3A/Projects/x/main.odin`. On .NET Framework `new Uri(uri).LocalPath` gives `/D:/Projects/x/main.odin`, and `File.ReadAllText` on that throws `System.NotSupportedException: The given path's format is not supported.`
Decode the URI yourself: `Uri.UnescapeDataString(uri.AbsolutePath)`, drop the leading `/` before a drive letter, swap `/` for `\`, and handle a non-empty `Host` as a UNC path. We had a tested helper for this (`FileUriPath`, removed again in the M2 branch once nothing used it; it is in commit `e0460c8`). Verified in the Exp instance on 2026-10-30 (log line `could not read /D:/Projects/...`).

## An exception in the diagnostics middle layer hides every diagnostic
`OlsMiddleLayer` sits between ols and VS. If `HandleNotificationAsync` throws before `sendNotification`, VS receives nothing: no squiggles and an empty Error List, while the log still shows ols publishing. The reshaping is therefore wrapped in a catch-all that logs and forwards the original. Keep any new reshaping inside that `try`, and catch `Exception` there, not just I/O exceptions. This cost several rebuild cycles on 2026-10-30 because the earlier catch covered only `IOException`.

## The Error List "Project" column cannot be filled from `publishDiagnostics`
`_vs_projects` (project name, context, identifier) is only converted for `VSDiagnostic` objects in VS's diagnostics broker (`RemoteDiagnosticsBrokerHelper.ConvertDiagnostics`, decompiled); that is the pull path (`textDocument/diagnostic`), which ols does not implement. With the package name added to each pushed diagnostic, the Error List Project column stayed empty (Exp, 2026-10-30, 0.2.11, log confirmed the package `main` was found). Not pursued further; revisit only if a project system supplies the project for `.odin` files.

## The Error List "Source" column is not the LSP `source` field
The Source column showed "IntelliSense" even when the middle layer set `source = "ols"`; the column is a VS category for language-server diagnostics. Setting `source` has no visible effect in the Error List, so it is not set. The Code column and the squiggle tooltip prefix do come from `code`.

## `&` does not wait for VSIXInstaller or devenv
`VSIXInstaller.exe` and `devenv.exe /updateconfiguration` are GUI-subsystem programs, so calling them with `&` in PowerShell returns before they finish. Launching Exp straight afterwards raced the install and left no Odin features (no `OnLoadedAsync` in `%LOCALAPPDATA%\OdinVS\odinvs.log`); a second launch happened to work, so we first worked around it with a launch, close, launch sequence. With `Start-Process -Wait` (as `scripts/exp-reinstall.ps1` does now) a single launch loaded the extension (observed twice, 2026-10-30). The race was suggested by a code review and fits this evidence; it was not proven in isolation.

## The Exp instance must be closed before reinstalling
The installer cannot replace the extension while an Exp `devenv.exe` holds it. The script asks Exp processes (matched by `/rootSuffix Exp` on the command line) to close, and force-kills them after 15 seconds. Unsaved edits to a sample open in Exp can otherwise end up in a commit, as happened with `broken/main.odin`.

## CPS reads `ProjectConfiguration` metadata unevaluated
`ProjectConfiguration` items whose metadata is computed (item transforms, `$([System.String]::...)`, even `$(Platform)`) show up as literal text in the Configuration Manager (`$([System.String]::C...`, `$(Platform)`), VS reports "Current solution contains unknown project configuration mappings", and Build is skipped ("Skipped Rebuild All"). `dotnet msbuild` evaluates the same expressions fine, so command-line builds and tier-3 tests pass. Found in Exp 18.11 on 2026-11-01 after the first M3 commit; the code came from the odin-targets-path spike, which had never been re-run in Exp after that change.
Fix: declare the items with literal metadata (`Debug|x64` and `Release|x64`, each conditional on `$(Configurations)`, in `Sdk.targets`). Consequence: custom configuration names are not shown in VS. Guarded by tier-3 test `ProjectConfigurationsAreLiteralDebugAndReleaseX64`. Whether a user-written literal `ProjectConfiguration` item in the `.odinproj` shows a custom name in VS is untested.

## A project that declares its own `ProjectConfiguration` items replaces the SDK's in VS
Observed in Exp 18.11 (2026-11-01) with `Sdk.targets` declaring conditional `Debug|x64` and `Release|x64` items: a project with `<Configurations>Debug;Profile</Configurations>` plus one literal `Profile|x64` item showed only `Profile` in the Configuration Manager (MSBuild itself evaluated Debug and Profile). A project with `Debug;Release;Profile` and a literal item for each showed all three, and a Profile solution configuration built it into `bin\Profile`. A project with `Configurations` set but no literal items still showed Debug and Release. So a custom configuration needs a literal item for every configuration the project should show; the reason (items from the SDK being ignored once the project declares any) is inferred from these runs, not from CPS source. See `docs/user-guide.md`, "Configurations".
