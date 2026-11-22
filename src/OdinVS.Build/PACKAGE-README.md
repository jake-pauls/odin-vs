# Odin.Sdk: LOCAL ONLY, DO NOT PUBLISH

This package is a private development build of the odin-vs MSBuild SDK.

- Do **not** push it to nuget.org, GitHub Packages or any other shared feed.
- It is built from source on each machine (`scripts\pack-sdk.ps1` into `nuget/` of the private `jake-pauls/odin-vs` repository) and is not committed.
- The `-local` version suffix marks it as unreleased. Publishing is a decision for the repository owner, made explicitly and later.

Use it from a folder feed: a `nuget.config` with `<clear />` and one source pointing at the `nuget/` directory, then
`<Project Sdk="Odin.Sdk/0.5.1-local">` in the `.odinproj`.
