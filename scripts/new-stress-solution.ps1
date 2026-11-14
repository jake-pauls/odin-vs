<#
.SYNOPSIS
  Generates a many-project Odin solution (M7 stress test) and optionally times loading, building and incremental building it.

.DESCRIPTION
  Writes <OutputDirectory>\StressNNN\StressNNN.odinproj (+ src\main.odin and one package) for NNN = 1..Count, a Stress.sln that holds
  them all, and a nuget.config that points at the local Odin.Sdk feed (run scripts\pack-sdk.ps1 first).
  Shapes (Stress001 is the root application in each):
    Tree  project i references 2i and 2i+1 (a balanced tree, about log2(Count) levels deep; the realistic case)
    Chain project i references i+1 (depth Count: the worst case for transitive collections and recompilation)
    Flat  project 1 references every other project (one project with Count-1 direct references)
  Every reference is a Source reference, so a project compiles all packages below it.
  The output folder is git-ignored when it is the default (stress\ in the repo root). Nothing here is part of the product.

.PARAMETER Measure
  After generating, run: cold restore + build (-m), a no-op build, a one-leaf touch + build, and a clean. Times go to the console and to
  <OutputDirectory>\timings.txt. Uses a scratch NuGet package cache so the SDK is read from the local feed, never a stale cached copy.
#>
param(
    [int]$Count = 100,
    [ValidateSet('Tree', 'Chain', 'Flat')][string]$Shape = 'Tree',
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'stress'),
    [string]$SdkVersion = '0.5.0-local',
    [switch]$Measure
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$utf8 = New-Object System.Text.UTF8Encoding($false)
$odinProjectType = '{7B1D0F42-3C9E-4E6A-8A15-5D2F9C3E71B8}'

function Name([int]$i) { 'Stress{0:D3}' -f $i }
function Guid([int]$i) { '{C5555555-0000-4000-8000-' + ('{0:D12}' -f $i) + '}' }

function ReferencesOf([int]$i) {
    switch ($Shape) {
        'Tree' { @((2 * $i), (2 * $i + 1)) | Where-Object { $_ -le $Count } }
        'Chain' { if ($i -lt $Count) { @($i + 1) } else { @() } }
        'Flat' { if ($i -eq 1) { 2..$Count } else { @() } }
    }
}

if (Test-Path $OutputDirectory) {
    throw "'$OutputDirectory' already exists; give a new -OutputDirectory or delete it yourself."
}

New-Item -ItemType Directory $OutputDirectory | Out-Null
$feed = Join-Path $repo 'nuget'
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'nuget.config'),
    "<configuration><packageSources><clear /><add key=""local"" value=""$feed"" /></packageSources></configuration>", $utf8)

foreach ($i in 1..$Count) {
    $name = Name $i
    $dir = Join-Path $OutputDirectory $name
    $package = 'pkg_{0:D3}' -f $i
    New-Item -ItemType Directory (Join-Path $dir "src\$package") | Out-Null
    $refs = @(ReferencesOf $i)

    $projectReferences = ($refs | ForEach-Object { "    <ProjectReference Include=""..\$(Name $_)\$(Name $_).odinproj"" />" }) -join "`r`n"
    $project = "<Project Sdk=""Odin.Sdk/$SdkVersion"">`r`n  <ItemGroup>`r`n    <OdinCompile Include=""src\**\*.odin"" />`r`n  </ItemGroup>`r`n"
    if ($refs.Count -gt 0) { $project += "  <ItemGroup>`r`n$projectReferences`r`n  </ItemGroup>`r`n" }
    $project += "</Project>`r`n"
    [IO.File]::WriteAllText((Join-Path $dir "$name.odinproj"), $project, $utf8)

    $imports = ($refs | ForEach-Object { 'import "{0}:src/pkg_{1:D3}"' -f (Name $_), $_ }) -join "`r`n"
    $terms = (@("$i") + ($refs | ForEach-Object { 'pkg_{0:D3}.value()' -f $_ })) -join ' + '
    [IO.File]::WriteAllText((Join-Path $dir "src\$package\$package.odin"), "package $package`r`n`r`n$imports`r`n`r`nvalue :: proc() -> int {`r`n`treturn $terms`r`n}`r`n", $utf8)
    [IO.File]::WriteAllText((Join-Path $dir 'src\main.odin'), "package main`r`n`r`nimport ""core:fmt""`r`nimport ""$package""`r`n`r`nmain :: proc() {`r`n`tfmt.println(""$name"", ${package}.value())`r`n}`r`n", $utf8)
}

$sln = "Microsoft Visual Studio Solution File, Format Version 12.00`r`n# Visual Studio Version 18`r`nVisualStudioVersion = 18.0.0.0`r`nMinimumVisualStudioVersion = 10.0.40219.1`r`n"
foreach ($i in 1..$Count) { $n = Name $i; $sln += "Project(""$odinProjectType"") = ""$n"", ""$n\$n.odinproj"", ""$(Guid $i)""`r`nEndProject`r`n" }
$sln += "Global`r`n`tGlobalSection(SolutionConfigurationPlatforms) = preSolution`r`n`t`tDebug|x64 = Debug|x64`r`n`t`tRelease|x64 = Release|x64`r`n`tEndGlobalSection`r`n`tGlobalSection(ProjectConfigurationPlatforms) = postSolution`r`n"
foreach ($i in 1..$Count) { foreach ($c in 'Debug', 'Release') { $sln += "`t`t$(Guid $i).$c|x64.ActiveCfg = $c|x64`r`n`t`t$(Guid $i).$c|x64.Build.0 = $c|x64`r`n" } }
$sln += "`tEndGlobalSection`r`n`tGlobalSection(SolutionProperties) = preSolution`r`n`t`tHideSolutionNode = FALSE`r`n`tEndGlobalSection`r`nEndGlobal`r`n"
$solutionPath = Join-Path $OutputDirectory 'Stress.sln'
[IO.File]::WriteAllText($solutionPath, $sln, $utf8)
Write-Host "Generated $Count projects ($Shape) in $OutputDirectory"

if (-not $Measure) {
    Write-Host "Open $solutionPath in Visual Studio, or run with -Measure."
    return
}

$env:NUGET_PACKAGES = Join-Path $OutputDirectory '.packages'
$timings = New-Object System.Collections.Generic.List[string]
function Time([string]$label, [scriptblock]$action) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $output = & $action 2>&1
    $watch.Stop()
    $failed = ($LASTEXITCODE -ne 0)
    $line = '{0,-34} {1,8:N1} s  {2}' -f $label, $watch.Elapsed.TotalSeconds, $(if ($failed) { 'FAILED' } else { 'ok' })
    Write-Host $line
    $timings.Add($line)
    if ($failed) { $output | Select-Object -Last 15 | ForEach-Object { Write-Host "  $_" } }
}

# -t:Build on purpose: the solution's default target is not what Visual Studio calls (see Sdk.props DefaultTargets, M6).
# A solution restore skips .odinproj projects (NU1503), so the SDK is installed into the scratch cache by evaluating one project first.
Time 'SDK restore (first evaluation)' { dotnet msbuild (Join-Path $OutputDirectory "$(Name 1)\$(Name 1).odinproj") -restore -getProperty:OutDir -nodeReuse:false -nologo }
# `dotnet msbuild` builds none of the projects of a solution that holds .odinproj files (it runs only ValidateSolutionConfiguration), so
# solutions are built with Visual Studio's MSBuild, as Visual Studio itself does. Assumes the machine's VS 18 Community install.
$vsMsBuild = 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe'
# Configuration and Platform are passed on purpose: without Platform=x64 the solution maps no project and builds nothing, silently.
$build = { & $vsMsBuild $solutionPath -t:Build -p:Configuration=Debug -p:Platform=x64 -m -nodeReuse:false -nologo -v:q }
Time 'cold build (-m)' $build
$built = @(Get-ChildItem $OutputDirectory -Recurse -Filter *.exe).Count
Write-Host "  executables produced: $built of $Count"
$timings.Add("  executables produced: $built of $Count")
if ($built -ne $Count) { throw "only $built of $Count projects produced an exe; see the build output above." }
Time 'no-op build (-m)' $build
$leaf = Join-Path $OutputDirectory "$(Name $Count)\src\pkg_$('{0:D3}' -f $Count)\pkg_$('{0:D3}' -f $Count).odin"
Add-Content -Path $leaf -Value "`r`n// touched"
Time 'build after touching one leaf' $build
Time 'clean' { & $vsMsBuild $solutionPath -t:Clean -p:Configuration=Debug -p:Platform=x64 -m -nodeReuse:false -nologo -v:q }
Time 'evaluate root only (-getItem)' { dotnet msbuild (Join-Path $OutputDirectory "$(Name 1)\$(Name 1).odinproj") -getItem:OdinCompile -nodeReuse:false -nologo }
[IO.File]::WriteAllLines((Join-Path $OutputDirectory 'timings.txt'), $timings, $utf8)
