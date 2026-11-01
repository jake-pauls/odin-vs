# nuget/: LOCAL ONLY, DO NOT PUBLISH

This folder is a local NuGet feed that `scripts\pack-sdk.ps1` writes the `Odin.Sdk` development package into (`Odin.Sdk.<version>-local.nupkg`). The `.nupkg` is git-ignored: build it on each machine, never commit it.

**The package must stay local and unpublished.** Never run `dotnet nuget push`, `nuget push`, or any upload of these files to nuget.org, GitHub Packages or any other feed.

- Build it after cloning and after changing anything under `src/OdinVS.Build` or `src/OdinVS.Core`: `scripts\pack-sdk.ps1`.
- Use it by pointing a `nuget.config` (with `<clear />`) at this folder, as `tests/samples/nuget.config` does.
- The package's own metadata (title, description, tags, `-local` version suffix, README) also says "local only", and packing prints warning `ODINPKG1`.
