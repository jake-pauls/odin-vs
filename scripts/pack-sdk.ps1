# Packs Odin.Sdk into nuget/ (LOCAL ONLY: never push the result anywhere). Run after changing src/OdinVS.Build or src/OdinVS.Core.
# NuGet caches restored SDKs per version under ~\.nuget\packages\odin.sdk\<version>; this script does not touch that cache.
# After repacking the same version, point NUGET_PACKAGES at a scratch folder (as the tier-3 tests do) or bump <Version>.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
dotnet pack "$root\src\OdinVS.Build\Odin.Sdk.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed" }
Write-Host "Packed into $root\nuget (LOCAL ONLY, do not publish). Delete the cached copy under ~\.nuget\packages\odin.sdk yourself if a same-version repack is not picked up."
