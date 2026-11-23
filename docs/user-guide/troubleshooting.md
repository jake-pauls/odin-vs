# Troubleshooting

| Symptom | Cause and fix |
|---|---|
| A new Odin project fails to load with an SDK-resolution error | NuGet cannot find `Odin.Sdk`. Run `scripts\pack-sdk.ps1`, add a `nuget.config` at or above the project that points at the clone's `nuget` folder, and make the `Sdk` attribute (`Odin.Sdk/<version>`) match the `nupkg` file name, which differs if you repacked another version. See [Getting started](../getting-started.md#3-build-the-local-odinsdk-package). |
| Edits to the SDK (after a `git pull`) have no effect | `~\.nuget\packages\odin.sdk\<version>` still holds the old copy. Delete that folder (or bump the SDK version) after repacking. |
| Build error `ODIN0101` | `odin.exe` was not found. Install Odin, put it on `PATH`, or set `OdinExe` in the project. |
| Link errors from the command line, or `link` not found | Build from a *Developer PowerShell for VS*; Odin's link step needs the MSVC environment. |
| An info bar says `odin.exe` or `ols.exe` is missing, or the Odin version differs from the bundled `ols` | The message says what to fix. *Open log* shows `%LOCALAPPDATA%\OdinVS\odinvs.log`. |
| Reinstalling the extension changes nothing | Visual Studio ignores a `.vsix` whose version is unchanged. Raise `Version` in `src\OdinVS\source.extension.vsixmanifest`, rebuild, reinstall. |
| Newly built DLLs or executables are blocked ("An Application Control policy has blocked this file") | Windows Smart App Control blocks unsigned build output. This is a system security setting; see [pitfalls.md](../dev/pitfalls.md) ("Smart App Control blocks freshly built binaries"). |
| A custom configuration is missing from the Configuration Manager | Declare it with `ProjectConfiguration` items; see [Configurations](../reference/configurations.md). |
| F5 does nothing for a project | A `Dll` or `Lib` project cannot be started; F5 says so. Start an executable project that uses it. |
| Breakpoints are not hit | Only Debug builds carry debug symbols (`-debug`). Build the Debug configuration, or set `<OdinDebug>true</OdinDebug>` for another configuration. |
| Static library (`OdinOutputType` `Lib`) fails to link | A known problem with the tested Odin version; see [Known limitations](limitations.md). |
