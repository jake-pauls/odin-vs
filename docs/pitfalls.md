# Pitfalls

Verified problems and their workarounds. Add new ones with how they were verified.

## `Uri.LocalPath` breaks on ols file URIs
ols sends drive-letter URIs with the colon percent-encoded: `file:///D%3A/Projects/x/main.odin`. On .NET Framework `new Uri(uri).LocalPath` gives `/D:/Projects/x/main.odin`, and `File.ReadAllText` on that throws `System.NotSupportedException: The given path's format is not supported.`
Use `FileUriPath.ToLocalPath` (`OdinVS.Core`, unit-tested) instead. Verified in the Exp instance on 2026-10-30 (log line `could not read /D:/Projects/...`).

## An exception in the diagnostics middle layer hides every diagnostic
`OlsMiddleLayer` sits between ols and VS. If `HandleNotificationAsync` throws before `sendNotification`, VS receives nothing: no squiggles and an empty Error List, while the log still shows ols publishing. The reshaping is therefore wrapped in a catch-all that logs and forwards the original. Keep any new reshaping inside that `try`, and catch `Exception` there, not just I/O exceptions. This cost several rebuild cycles on 2026-10-30 because the earlier catch covered only `IOException`.

## The Error List "Project" column cannot be filled from `publishDiagnostics`
`_vs_projects` (project name, context, identifier) is only converted for `VSDiagnostic` objects in VS's diagnostics broker (`RemoteDiagnosticsBrokerHelper.ConvertDiagnostics`, decompiled); that is the pull path (`textDocument/diagnostic`), which ols does not implement. With the package name added to each pushed diagnostic, the Error List Project column stayed empty (Exp, 2026-10-30, 0.2.11, log confirmed the package `main` was found). Not pursued further; revisit only if a project system supplies the project for `.odin` files.

## The Error List "Source" column is not the LSP `source` field
The Source column showed "IntelliSense" even when the middle layer set `source = "ols"`; the column is a VS category for language-server diagnostics. Setting `source` has no visible effect in the Error List, so it is not set. The Code column and the squiggle tooltip prefix do come from `code`.

## First launch after an install does not load the extension
After `VSIXInstaller /rootSuffix:Exp`, the first Exp launch left no Odin features (no `OnLoadedAsync` in `%LOCALAPPDATA%\OdinVS\odinvs.log`). Closing it, running `devenv /rootSuffix Exp /updateconfiguration` again and launching a second time on an `.odin` file worked every time (0.2.5, 0.2.9, 0.2.11). A bare warm-up launch without a file did not. The cause is not known. `scripts/exp-reinstall.ps1` does the sequence.

## The Exp instance must be closed before reinstalling
The installer cannot replace the extension while an Exp `devenv.exe` holds it. The script closes Exp processes (matched by `/rootSuffix Exp` on the command line) first.
