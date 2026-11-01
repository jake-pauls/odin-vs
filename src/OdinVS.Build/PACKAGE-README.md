# Odin.Sdk: LOCAL ONLY, DO NOT PUBLISH

This package is a private development build of the odin-vs MSBuild SDK.

- Do **not** push it to nuget.org, GitHub Packages or any other shared feed.
- It is checked into the private `jake-pauls/odin-vs` repository (`feed/`) only so it can be tested on another of the owner's machines.
- The `-local` version suffix marks it as unreleased. Publishing is a decision for the repository owner, made explicitly and later.

Use it from a folder feed: a `nuget.config` with `<clear />` and one source pointing at the `feed/` directory, then
`<Project Sdk="Odin.Sdk/0.3.0-local">` in the `.odinproj`.
