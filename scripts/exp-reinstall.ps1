# Rebuilds the VSIX, reinstalls it into the Visual Studio Experimental instance and launches that instance on a file.
# Usage: scripts\exp-reinstall.ps1 [-OpenFile <path>] [-Bump]
#   -Bump   increments the patch part of the manifest version first (VS ignores a reinstall with an unchanged version)
param(
    [string] $OpenFile = 'tests\samples\LanguageServer\broken\main.odin',
    [switch] $Bump
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$vs = 'C:\Program Files\Microsoft Visual Studio\18\Community'
$devenv = Join-Path $vs 'Common7\IDE\devenv.exe'
$manifest = Join-Path $repo 'src\OdinVS\source.extension.vsixmanifest'
$vsix = Join-Path $repo 'src\OdinVS\bin\Debug\net472\OdinVS.vsix'
$log = Join-Path $env:LOCALAPPDATA 'OdinVS\odinvs.log'

function Stop-Exp {
    Get-CimInstance Win32_Process -Filter "Name = 'devenv.exe'" |
        Where-Object { $_.CommandLine -match '/rootSuffix\s+Exp' } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
    Start-Sleep -Seconds 3
}

if ($Bump) {
    $text = Get-Content $manifest -Raw
    $text = [regex]::Replace($text, '(<Identity [^>]*Version="\d+\.\d+\.)(\d+)"', { param($m) $m.Groups[1].Value + ([int]$m.Groups[2].Value + 1) + '"' })
    [System.IO.File]::WriteAllText($manifest, $text, (New-Object System.Text.UTF8Encoding($false)))
}

Stop-Exp

& (Join-Path $vs 'MSBuild\Current\Bin\MSBuild.exe') (Join-Path $repo 'src\OdinVS\OdinVS.csproj') /restore /t:Rebuild /p:Configuration=Debug /nologo /v:m
if ($LASTEXITCODE -ne 0) { throw "build failed ($LASTEXITCODE)" }

& (Join-Path $vs 'Common7\IDE\VSIXInstaller.exe') /quiet /rootSuffix:Exp $vsix
& $devenv /rootSuffix Exp /updateconfiguration

$target = if ([System.IO.Path]::IsPathRooted($OpenFile)) { $OpenFile } else { Join-Path $repo $OpenFile }

# Observed: the first launch after an install does not load the extension (no log entry, no Odin features). Closing it,
# refreshing the configuration again and launching a second time on the file does. A bare warm-up launch did not help.
Start-Process $devenv -ArgumentList '/rootSuffix', 'Exp', "`"$target`"" | Out-Null
Start-Sleep -Seconds 40
Stop-Exp
& $devenv /rootSuffix Exp /updateconfiguration
Start-Process $devenv -ArgumentList '/rootSuffix', 'Exp', "`"$target`"" | Out-Null
Start-Sleep -Seconds 30

if (Test-Path $log) { Get-Content $log -Tail 6 }
