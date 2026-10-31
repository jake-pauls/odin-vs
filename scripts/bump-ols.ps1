# Replaces the bundled ols with the one from a local Odin install and keeps every place that names it in sync:
# src\OdinVS\ols\ols.exe, builtin\*.odin, ols.pin (version + SHA-256), BundledOls.cs and THIRD-PARTY-NOTICES.md.
# Then runs the tier-2 ols tests, which check that the binary reports the pinned version.
# Usage: scripts\bump-ols.ps1 [-Version dev-2026-10] [-Source <ols folder>] [-Odin <odin.exe>] [-SkipTests]
#   -Version    the Odin release this ols belongs to; read from `odin version` when omitted
#   -Source     folder holding ols-x86_64-pc-windows-msvc.exe and builtin\ (defaults to this machine's Odin install)
param(
    [string] $Version,
    [string] $Source = 'C:\Program Files\Odin\ols',
    [string] $Odin = 'C:\Program Files\Odin\odin\dist\odin.exe',
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$bundle = Join-Path $repo 'src\OdinVS\ols'
$utf8 = New-Object System.Text.UTF8Encoding($false)

if (-not $Version) {
    $reported = (& $Odin version) -join ' '
    if ($reported -notmatch 'dev-(\d{4})-(\d{2})') { throw "Could not read a dev-YYYY-MM version from '$Odin version' ($reported); pass -Version." }
    $Version = "dev-$($Matches[1])-$($Matches[2])"
    Write-Host "Using version $Version from '$Odin version'."
}
if ($Version -notmatch '^dev-(\d{4})-(\d{2})$') { throw "Version must look like dev-2026-10, got '$Version'." }
$year = [int] $Matches[1]
$month = [int] $Matches[2]

$olsSource = Join-Path $Source 'ols-x86_64-pc-windows-msvc.exe'
if (-not (Test-Path $olsSource)) { throw "Not found: $olsSource" }
Copy-Item $olsSource (Join-Path $bundle 'ols.exe') -Force
Copy-Item (Join-Path $Source 'builtin\*.odin') (Join-Path $bundle 'builtin') -Force

$hash = (Get-FileHash (Join-Path $bundle 'ols.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
$pin = @(
    '# Pinned bundled ols. The build and the unit tests fail if ols.exe no longer matches this file.',
    '# Change it only with scripts/bump-ols.ps1, which updates the version, hash, BundledOls.cs and THIRD-PARTY-NOTICES.md together.',
    "version=$Version",
    "sha256=$hash"
) -join "`n"
[System.IO.File]::WriteAllText((Join-Path $bundle 'ols.pin'), $pin + "`n", $utf8)

$constants = Join-Path $repo 'src\OdinVS.Core\BundledOls.cs'
$text = [System.IO.File]::ReadAllText($constants)
$text = [regex]::Replace($text, 'new\(\d+, \d+\)', "new($year, $month)")
[System.IO.File]::WriteAllText($constants, $text, $utf8)

$notices = Join-Path $repo 'THIRD-PARTY-NOTICES.md'
$text = [System.IO.File]::ReadAllText($notices)
foreach ($name in 'ols.exe', 'builtin.odin', 'intrinsics.odin') {
    $path = if ($name -eq 'ols.exe') { Join-Path $bundle $name } else { Join-Path $bundle "builtin\$name" }
    $fileHash = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
    $text = [regex]::Replace($text, "(``$([regex]::Escape($name))`` ``)[0-9a-f]{64}(``)", "`${1}$fileHash`${2}")
}
$text = [regex]::Replace($text, 'which reports `dev-\d{4}-\d{2}`', "which reports ``$Version``")
[System.IO.File]::WriteAllText($notices, $text, $utf8)

Write-Host "Pinned ols $Version, sha256 $hash"
if (-not $SkipTests) {
    dotnet test (Join-Path $repo 'tests\OdinVS.Core.Tests') --nologo -v q --filter 'TestCategory=RequiresToolchain|FullyQualifiedName~BundledOlsPinTests'
    if ($LASTEXITCODE -ne 0) { throw "tests failed; the bundled ols does not report $Version or the pin is out of sync." }
}
git -C $repo status --short
