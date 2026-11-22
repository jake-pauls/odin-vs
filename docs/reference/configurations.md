# Configurations

`Configurations` is read like in a `.csproj`, so the command line accepts any name you list, for example `<Configurations>Debug;Release;Profile</Configurations>` and `msbuild /p:Configuration=Profile`. Only `Debug` adds `-debug`; other names build without it, so use `OdinExtraArgs` for anything else.

Visual Studio learns a project's configurations from `ProjectConfiguration` items, not from the `Configurations` property. The SDK declares `Debug|x64` and `Release|x64` for you, so a custom name listed only in `Configurations` builds from the command line but is **not** shown in the Configuration Manager or the toolbar.

To make a custom configuration appear in VS, declare literal items in the `.odinproj` for **every** configuration the project should show, including Debug and Release (tried in Visual Studio 18.11: with only a `Profile` item, VS offered just `Profile`; with all three, it offered all three and a Profile solution configuration built into `bin\Profile`):

```xml
<PropertyGroup>
  <Configurations>Debug;Release;Profile</Configurations>
</PropertyGroup>
<ItemGroup>
  <ProjectConfiguration Include="Debug|x64"><Configuration>Debug</Configuration><Platform>x64</Platform></ProjectConfiguration>
  <ProjectConfiguration Include="Release|x64"><Configuration>Release</Configuration><Platform>x64</Platform></ProjectConfiguration>
  <ProjectConfiguration Include="Profile|x64"><Configuration>Profile</Configuration><Platform>x64</Platform></ProjectConfiguration>
</ItemGroup>
```

The values must be plain text: VS reads them without evaluating `$(...)` (see [pitfalls.md](../pitfalls.md)). A solution configuration of the same name still has to be created in the Configuration Manager (or the `.sln`), and each project mapped to it; a project that does not declare that name is skipped when that solution configuration builds.
