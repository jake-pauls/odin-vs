# Rebuilds the VSIX, reinstalls it into the Visual Studio Experimental instance and launches that instance on a file.
# Usage: scripts\exp-reinstall.ps1 [-OpenFile <path>] [-Bump] [-VsRoot <path>]
#   -Bump    increments the patch part of the manifest version first (VS ignores a reinstall with an unchanged version)
#   -VsRoot  the Visual Studio install to build with and install into (defaults to this machine's VS 2026 Community)
param(
    [string] $OpenFile = 'tests\samples\LanguageServer\broken\main.odin',
    [switch] $Bump,
    [string] $VsRoot = 'C:\Program Files\Microsoft Visual Studio\18\Community'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$vs = $VsRoot
$devenv = Join-Path $vs 'Common7\IDE\devenv.exe'
$manifest = Join-Path $repo 'src\OdinVS\source.extension.vsixmanifest'
$vsix = Join-Path $repo 'src\OdinVS\bin\Debug\net472\OdinVS.vsix'
$log = Join-Path $env:LOCALAPPDATA 'OdinVS\odinvs.log'

# Asks Exp to close (so it can prompt about unsaved edits) and only kills it if it is still there after $GraceSeconds.
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
    $text = Get-Content $manifest -Raw -Encoding UTF8
    $text = [regex]::Replace($text, '(<Identity [^>]*Version="\d+\.\d+\.)(\d+)"', { param($m) $m.Groups[1].Value + ([int]$m.Groups[2].Value + 1) + '"' })
    [System.IO.File]::WriteAllText($manifest, $text, (New-Object System.Text.UTF8Encoding($false)))
}

Stop-Exp

& (Join-Path $vs 'MSBuild\Current\Bin\MSBuild.exe') (Join-Path $repo 'src\OdinVS\OdinVS.csproj') /restore /t:Rebuild /p:Configuration=Debug /nologo /v:m
if ($LASTEXITCODE -ne 0) { throw "build failed ($LASTEXITCODE)" }

$installExit = Invoke-Gui (Join-Path $vs 'Common7\IDE\VSIXInstaller.exe') @('/quiet', '/rootSuffix:Exp', "`"$vsix`"")
if ($installExit -ne 0) { throw "VSIXInstaller exited with $installExit" }
[void] (Invoke-Gui $devenv @('/rootSuffix', 'Exp', '/updateconfiguration'))

$target = if ([System.IO.Path]::IsPathRooted($OpenFile)) { $OpenFile } else { Join-Path $repo $OpenFile }
Start-Process $devenv -ArgumentList '/rootSuffix', 'Exp', "`"$target`"" | Out-Null
Start-Sleep -Seconds 30

if (Test-Path $log) { Get-Content $log -Tail 6 }
