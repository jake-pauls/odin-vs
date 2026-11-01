# feed/: LOCAL ONLY, DO NOT PUBLISH

This folder is a local NuGet feed holding the `Odin.Sdk` development package (`Odin.Sdk.<version>-local.nupkg`).

**The package must stay local and unpublished.** Never run `dotnet nuget push`, `nuget push`, or any upload of these files to nuget.org, GitHub Packages or any other feed.

Why it is committed: the repository is private and the owner needs to test builds on another machine without packing first. That is the only reason.

- Rebuild it with `scripts\pack-sdk.ps1` after changing anything under `src/OdinVS.Build` or `src/OdinVS.Core`; commit the new file with the change.
- Use it by pointing a `nuget.config` (with `<clear />`) at this folder.
- The package's own metadata (title, description, tags, `-local` version suffix, README) also says "local only", and packing prints warning `ODINPKG1`.
- If this repository is ever made public, remove the `.nupkg` files first.
