# Rebuilds the M5.5 spike VSIX, installs it into the Visual Studio Experimental instance (next to the product VSIX,
# which must already be installed there: scripts\exp-reinstall.ps1) and launches Exp on the sample solution.
# Usage: spikes\concord-visualizer\exp-install-spike.ps1 [-Bump] [-NoLaunch] [-VsRoot <path>]
#   -Bump  increments the patch part of the spike manifest version first (VS ignores a reinstall with an unchanged version)
param(
    [switch] $Bump,
    [switch] $NoLaunch,
    [string] $VsRoot = 'C:\Program Files\Microsoft Visual Studio\18\Community'
)

$ErrorActionPreference = 'Stop'
$spike = $PSScriptRoot
$devenv = Join-Path $VsRoot 'Common7\IDE\devenv.exe'
$project = Join-Path $spike 'OdinConcordSpike\OdinConcordSpike.csproj'
$manifest = Join-Path $spike 'OdinConcordSpike\source.extension.vsixmanifest'
$vsix = Join-Path $spike 'OdinConcordSpike\bin\Debug\net472\OdinConcordSpike.vsix'
$sample = Join-Path $spike 'sample\ConcordSample.sln'
$log = Join-Path $env:LOCALAPPDATA 'OdinVS\concord-spike.log'

function Stop-Exp([int] $GraceSeconds = 15) {
    $exp = @(Get-CimInstance Win32_Process -Filter "Name = 'devenv.exe'" |
        Where-Object { $_.CommandLine -match '/rootSuffix\s+Exp' } |
        ForEach-Object { Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue })
    foreach ($process in $exp) { [void] $process.CloseMainWindow() }
    foreach ($process in $exp) {
        if (-not $process.WaitForExit($GraceSeconds * 1000)) { Stop-Process -Id $process.Id -Force }
    }
}

# VSIXInstaller and devenv are GUI-subsystem programs, so `&` returns before they finish; Start-Process -Wait does not.
function Invoke-Gui([string] $Path, [string[]] $Arguments) {
    $process = Start-Process $Path -ArgumentList $Arguments -Wait -PassThru
    return $process.ExitCode
}

if ($Bump) {
    $text = [System.IO.File]::ReadAllText($manifest)
    $text = [regex]::Replace($text, '(<Identity [^>]*Version="\d+\.\d+\.)(\d+)"', { param($m) $m.Groups[1].Value + ([int]$m.Groups[2].Value + 1) + '"' })
    [System.IO.File]::WriteAllText($manifest, $text, (New-Object System.Text.UTF8Encoding($false)))
}

Stop-Exp

& (Join-Path $VsRoot 'MSBuild\Current\Bin\MSBuild.exe') $project -restore -t:Rebuild -p:Configuration=Debug -nologo -v:m
if ($LASTEXITCODE -ne 0) { throw "build failed ($LASTEXITCODE)" }

$installExit = Invoke-Gui (Join-Path $VsRoot 'Common7\IDE\VSIXInstaller.exe') @('/quiet', '/rootSuffix:Exp', "`"$vsix`"")
if ($installExit -ne 0) { throw "VSIXInstaller exited with $installExit" }
[void] (Invoke-Gui $devenv @('/rootSuffix', 'Exp', '/updateconfiguration'))

if (-not $NoLaunch) {
    Start-Process $devenv -ArgumentList '/rootSuffix', 'Exp', "`"$sample`"" | Out-Null
}
"Spike installed. Component log: $log"
