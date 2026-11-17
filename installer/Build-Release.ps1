# Builds the self-contained Windows x64 release ZIP that Install.cmd expects:
#   Install.cmd, Install.ps1, Install-Hooks.ps1, Disconnect-Hooks.ps1,
#   Uninstall.cmd, Uninstall.ps1, README.md, LICENSE, app\SecretaryOverlay.exe ...
# Keep this file ASCII-only: Windows PowerShell 5.1 misreads BOM-less UTF-8.
param(
    [string]$Version = '',
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist')
)
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'SecretaryOverlay.csproj'
if (-not $Version) {
    $Version = ([xml](Get-Content -Raw -LiteralPath $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3 (got '$Version')." }

$output = [IO.Path]::GetFullPath($OutputDirectory)
$staging = Join-Path $output 'SecretaryOverlay-win-x64'
$zip = Join-Path $output 'SecretaryOverlay-win-x64.zip'
if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
New-Item -ItemType Directory -Path $staging -Force | Out-Null

dotnet publish $project -c Release -r win-x64 --self-contained true -nologo `
    -o (Join-Path $staging 'app') "-p:Version=$Version"
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

foreach ($name in @('Install.cmd', 'Install.ps1', 'Install-Hooks.ps1', 'Disconnect-Hooks.ps1', 'Uninstall.cmd', 'Uninstall.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $staging $name)
}
foreach ($name in @('README.md', 'LICENSE')) {
    Copy-Item -LiteralPath (Join-Path $repo $name) -Destination (Join-Path $staging $name)
}

# Compress-Archive on Windows PowerShell 5.1 writes '\' into entry names, which
# some extractors treat as part of the file name. Write '/' explicitly.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    Get-ChildItem -LiteralPath $staging -Recurse -File | ForEach-Object {
        $entry = $_.FullName.Substring($staging.Length + 1).Replace('\', '/')
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $entry, [IO.Compression.CompressionLevel]::Optimal)
    }
}
finally { $archive.Dispose() }
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
Set-Content -LiteralPath ($zip + '.sha256') -Value ($hash + '  SecretaryOverlay-win-x64.zip') -Encoding ascii
Write-Output ('Version ' + $Version)
Write-Output ('Package ' + $zip)
Write-Output ('SHA-256 ' + $hash)
