# M5.5 Phase F1: builds the unchanged product VSIX and the spike component, adds the component to a COPY of the product
# VSIX (dll, compiled .vsdconfig, DebuggerEngineExtension asset, content type) and writes it to
# spikes\concord-visualizer\OdinConcordSpike\bin\merged\OdinVS.Merged.vsix. Nothing under src\ is changed.
# Usage: spikes\concord-visualizer\merge-vsix-test.ps1 [-Install] [-NoLaunch] [-VsRoot <path>]
#   -Solution  the .sln Exp opens after the install (default: sample\ConcordSample.sln)
#   -KeepNatvisString  leave the product's `string` natvis entry in the copy (default: the copy ships an empty natvis)
#   -VersionBump  how far above the product's patch version the merged copy is (VS ignores a reinstall of the same version)
#   -Install   also removes the spike VSIX from Exp, installs ONLY the merged VSIX there and launches Exp on the sample
param(
    [switch] $Install,
    [switch] $NoLaunch,
    [string] $Solution = '',
    [int] $VersionBump = 1,
    [switch] $KeepNatvisString,
    [string] $VsRoot = 'C:\Program Files\Microsoft Visual Studio\18\Community'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$spike = $PSScriptRoot
$repo = Split-Path -Parent (Split-Path -Parent $spike)
$msbuild = Join-Path $VsRoot 'MSBuild\Current\Bin\MSBuild.exe'
$devenv = Join-Path $VsRoot 'Common7\IDE\devenv.exe'
$installer = Join-Path $VsRoot 'Common7\IDE\VSIXInstaller.exe'
$spikeBin = Join-Path $spike 'OdinConcordSpike\bin\Debug\net472'
$productVsix = Join-Path $repo 'src\OdinVS\bin\Debug\net472\OdinVS.vsix'
$outDir = Join-Path $spike 'OdinConcordSpike\bin\merged'
$merged = Join-Path $outDir 'OdinVS.Merged.vsix'
$spikeId = 'OdinConcordSpike.3935ebb4-2474-47a2-97fa-d7d03ce026e5'
$sample = if ($Solution) { $Solution } else { Join-Path $spike 'sample\ConcordSample.sln' }
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Stop-Exp([int] $GraceSeconds = 15) {
    $exp = @(Get-CimInstance Win32_Process -Filter "Name = 'devenv.exe'" |
        Where-Object { $_.CommandLine -match '/rootSuffix\s+Exp' } |
        ForEach-Object { Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue })
    foreach ($process in $exp) { [void] $process.CloseMainWindow() }
    foreach ($process in $exp) {
        if (-not $process.WaitForExit($GraceSeconds * 1000)) { Stop-Process -Id $process.Id -Force }
    }
}

function Invoke-Gui([string] $Path, [string[]] $Arguments) {
    $process = Start-Process $Path -ArgumentList $Arguments -Wait -PassThru
    return $process.ExitCode
}

function Read-Entry([IO.Compression.ZipArchive] $Zip, [string] $Name) {
    $reader = New-Object IO.StreamReader($Zip.GetEntry($Name).Open(), $utf8)
    try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
}

function Add-FileEntry([IO.Compression.ZipArchive] $Zip, [string] $Name, [string] $Path) {
    $entry = $Zip.CreateEntry($Name, [IO.Compression.CompressionLevel]::Optimal)
    $stream = $entry.Open()
    try { $bytes = [IO.File]::ReadAllBytes($Path); $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
}

Stop-Exp

& $msbuild (Join-Path $spike 'OdinConcordSpike\OdinConcordSpike.csproj') -restore -t:Rebuild -p:Configuration=Debug -nologo -v:m
if ($LASTEXITCODE -ne 0) { throw "spike build failed ($LASTEXITCODE)" }
& $msbuild (Join-Path $repo 'src\OdinVS\OdinVS.csproj') -restore -t:Rebuild -p:Configuration=Debug -nologo -v:m
if ($LASTEXITCODE -ne 0) { throw "product build failed ($LASTEXITCODE)" }

New-Item -ItemType Directory -Force $outDir | Out-Null
if (Test-Path $merged) { Remove-Item $merged -Force }

# Copy the product VSIX entry by entry (so manifest.json/catalog.json, which list the original files, are left out),
# changing the manifest and content types and adding the component.
$source = [IO.Compression.ZipFile]::OpenRead($productVsix)
$target = [IO.Compression.ZipFile]::Open($merged, 'Create')
try {
    foreach ($entry in $source.Entries) {
        if ($entry.FullName -in 'extension.vsixmanifest', '[Content_Types].xml', 'manifest.json', 'catalog.json') { continue }
        if ($entry.FullName -eq 'Visualizers/odin.natvis' -and -not $KeepNatvisString) {
            # The component shows Odin strings itself (behind the Odin gate); the global natvis entry also changed C++ types named `string`.
            $copy = $target.CreateEntry($entry.FullName)
            $w = New-Object IO.StreamWriter($copy.Open(), $utf8)
            $w.Write('<?xml version="1.0" encoding="utf-8"?>' + "`r`n" + '<!-- string display moved into the Concord component (M5.5 F4) -->' + "`r`n" + '<AutoVisualizer xmlns="http://schemas.microsoft.com/vstudio/debugger/natvis/2010">' + "`r`n" + '</AutoVisualizer>' + "`r`n")
            $w.Dispose()
            continue
        }
        $copy = $target.CreateEntry($entry.FullName, [IO.Compression.CompressionLevel]::Optimal)
        $in = $entry.Open(); $out = $copy.Open()
        try { $in.CopyTo($out) } finally { $in.Dispose(); $out.Dispose() }
    }

    $manifest = Read-Entry $source 'extension.vsixmanifest'
    $before = $manifest
    $oldVersion = [regex]::Match($manifest, '<Identity [^>]*Version="([^"]+)"').Groups[1].Value
    $parts = $oldVersion.Split('.')
    $parts[$parts.Length - 1] = [string]([int]$parts[$parts.Length - 1] + $VersionBump)
    $newVersion = $parts -join '.'
    $manifest = $manifest.Replace('Version="' + $oldVersion + '" Language', 'Version="' + $newVersion + '" Language')
    $manifest = $manifest.Replace('</Assets>', '  <Asset Type="DebuggerEngineExtension" Path="OdinConcordSpike.vsdconfig" />' + "`r`n  </Assets>")
    if ($manifest -eq $before -or -not $manifest.Contains('DebuggerEngineExtension')) { throw 'manifest was not changed as expected' }
    $e = $target.CreateEntry('extension.vsixmanifest'); $w = New-Object IO.StreamWriter($e.Open(), $utf8); $w.Write($manifest); $w.Dispose()

    $types = Read-Entry $source '[Content_Types].xml'
    if (-not $types.Contains('Extension="vsdconfig"')) {
        $types = $types.Replace('</Types>', '<Default Extension="vsdconfig" ContentType="application/octet-stream" /></Types>')
    }
    $e = $target.CreateEntry('[Content_Types].xml'); $w = New-Object IO.StreamWriter($e.Open(), $utf8); $w.Write($types); $w.Dispose()

    # The setup-engine catalog lists the original files and version; keep it consistent with the changed manifest.
    foreach ($name in 'manifest.json', 'catalog.json') {
        $json = Read-Entry $source $name
        $json = $json.Replace($oldVersion, $newVersion)
        if ($name -eq 'manifest.json') {
            $extra = '{"fileName":"/OdinConcordSpike.dll","sha256":null},{"fileName":"/OdinConcordSpike.vsdconfig","sha256":null}'
            if (-not $json.Contains('"files":[')) { throw 'manifest.json has no files list' }
            $json = $json.Replace('"files":[', '"files":[' + $extra + ',')
        }
        $e = $target.CreateEntry($name); $w = New-Object IO.StreamWriter($e.Open(), $utf8); $w.Write($json); $w.Dispose()
    }

    Add-FileEntry $target 'OdinConcordSpike.dll' (Join-Path $spikeBin 'OdinConcordSpike.dll')
    Add-FileEntry $target 'OdinConcordSpike.vsdconfig' (Join-Path $spikeBin 'OdinConcordSpike.vsdconfig')
}
finally {
    $target.Dispose(); $source.Dispose()
}

"Merged VSIX written: $merged"
$check = [IO.Compression.ZipFile]::OpenRead($merged)
"entries: " + $check.Entries.Count + "; manifest version: " + ([regex]::Match((Read-Entry $check 'extension.vsixmanifest'), '<Identity [^>]*Version="([^"]+)"').Groups[1].Value)
$check.Dispose()

if ($Install) {
    # The spike VSIX is a separate extension; the merged test must run with it gone (F1), or the component would load twice.
    $uninstallExit = Invoke-Gui $installer @('/quiet', '/rootSuffix:Exp', "/uninstall:$spikeId")
    "spike VSIX uninstall exit code: $uninstallExit"
    $installExit = Invoke-Gui $installer @('/quiet', '/rootSuffix:Exp', "`"$merged`"")
    if ($installExit -ne 0) { throw "VSIXInstaller exited with $installExit" }
    [void] (Invoke-Gui $devenv @('/rootSuffix', 'Exp', '/updateconfiguration'))
    if (-not $NoLaunch) { Start-Process $devenv -ArgumentList '/rootSuffix', 'Exp', "`"$sample`"" | Out-Null }
}
