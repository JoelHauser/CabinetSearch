<#
.SYNOPSIS
    Builds Cabinet Looter, checks its version, packs the release zip, and optionally installs it.

.DESCRIPTION
    Run this through PowerShell, not Bash: Bash mangles 'H:\SPT4.1.X' into 'H:SPT4.1.X'.

    The zip is unpacked over the SPT root, so it carries the full path BepInEx/plugins/... .
    Entries are written by hand with forward slashes: PowerShell 5.1's Compress-Archive writes
    backslashes, which some extractors turn into file names instead of folders.

.PARAMETER SPTPath
    The SPT install root to compile against (and install into, with -Install).

.PARAMETER Install
    Also copy the built plugin into the install's BepInEx\plugins.

.EXAMPLE
    scripts\pack.ps1 -SPTPath H:\SPT4.1.X
    scripts\pack.ps1 -SPTPath H:\SPT4.1.X -Install
#>
param(
    [Parameter(Mandatory = $true)][string] $SPTPath,
    [switch] $Install
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\CabinetLooter\CabinetLooter.csproj'
$source = Join-Path $root 'src\CabinetLooter\CabinetLooterPlugin.cs'

if (-not (Test-Path (Join-Path $SPTPath 'BepInEx\core\BepInEx.dll'))) {
    throw "SPTPath '$SPTPath' is not an SPT install root: no BepInEx\core\BepInEx.dll."
}

# ---- version agreement -----------------------------------------------------------
# The version lives in two places and they have to agree, or a release ships a DLL whose
# logged version is not the one on the zip.
$csprojVersion = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ }
$sourceVersion = (Select-String -Path $source -Pattern 'PluginVersion\s*=\s*"([^"]+)"').Matches[0].Groups[1].Value
if ($csprojVersion -ne $sourceVersion) {
    throw "Version mismatch: csproj says '$csprojVersion', CabinetLooterPlugin.PluginVersion says '$sourceVersion'."
}
$version = $csprojVersion
Write-Host "Version $version" -ForegroundColor Cyan

# ---- build -----------------------------------------------------------------------
dotnet build $project -c Release "-p:SPTPath=$SPTPath" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$built = Join-Path $root 'src\CabinetLooter\bin\Release\CabinetLooter.dll'

# ---- zip -------------------------------------------------------------------------
$entries = [ordered]@{
    'BepInEx/plugins/CabinetLooter.dll' = $built
    'CabinetLooter-README.md'           = (Join-Path $root 'README.md')
}

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
# Backport branch: the suffix keeps this zip apart from the 4.1.x release of the same version.
$zip = Join-Path $dist "CabinetLooter-$version-SPT4.0.x.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }

$archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($e in $entries.GetEnumerator()) {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $e.Value, $e.Key, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally {
    $archive.Dispose()
}

$check = [System.IO.Compression.ZipFile]::OpenRead($zip)
try {
    $names = $check.Entries | ForEach-Object { $_.FullName }
}
finally {
    $check.Dispose()
}
$missing = $entries.Keys | Where-Object { $names -notcontains $_ }
if ($missing) { throw "Zip is missing: $($missing -join ', ')" }
Write-Host "Packed $zip" -ForegroundColor Green
$names | ForEach-Object { Write-Host "  $_" }

# ---- install ---------------------------------------------------------------------
if ($Install) {
    $dest = Join-Path $SPTPath 'BepInEx\plugins'
    Copy-Item $built $dest -Force
    Write-Host "Installed to $dest" -ForegroundColor Green
}
