param(
    [string]$InstallDirectory = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\SecretaryOverlay'),
    [string]$HooksPath = '',
    [switch]$SkipShortcut,
    [switch]$NoStart
)
$ErrorActionPreference = 'Stop'

$sourceApp = Join-Path $PSScriptRoot 'app'
$sourceExe = Join-Path $sourceApp 'SecretaryOverlay.exe'
if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) {
    throw 'The release package is incomplete: app\SecretaryOverlay.exe is missing.'
}

$destination = [IO.Path]::GetFullPath($InstallDirectory)
$installedExe = Join-Path $destination 'SecretaryOverlay.exe'
$sourceFull = [IO.Path]::GetFullPath($sourceApp).TrimEnd('\')
$destinationFull = $destination.TrimEnd('\')
if ($destinationFull.Equals($sourceFull, [StringComparison]::OrdinalIgnoreCase) -or
    $destinationFull.StartsWith($sourceFull + '\', [StringComparison]::OrdinalIgnoreCase) -or
    $sourceFull.StartsWith($destinationFull + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Choose an installation folder outside the extracted release package.'
}

if (Test-Path -LiteralPath $installedExe -PathType Leaf) {
    $running = @(Get-CimInstance Win32_Process -Filter "name = 'SecretaryOverlay.exe'" |
        Where-Object { $_.ExecutablePath -eq $installedExe -and $_.CommandLine -notmatch '--hook' })
    if ($running.Count -gt 0) {
        Start-Process -FilePath $installedExe -ArgumentList '--quit' -Wait -WindowStyle Hidden
        $deadline = (Get-Date).AddSeconds(15)
        do {
            $running = @(Get-CimInstance Win32_Process -Filter "name = 'SecretaryOverlay.exe'" |
                Where-Object { $_.ExecutablePath -eq $installedExe -and $_.CommandLine -notmatch '--hook' })
            if ($running.Count -eq 0) { break }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        if ($running.Count -gt 0) { throw 'Close the running Secretary Overlay and run the installer again.' }
    }
}

New-Item -ItemType Directory -Path $destination -Force | Out-Null
Get-ChildItem -LiteralPath $sourceApp -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $destination -Recurse -Force
}
foreach ($script in @('Install-Hooks.ps1', 'Disconnect-Hooks.ps1', 'Uninstall.ps1', 'Uninstall.cmd')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $script) -Destination (Join-Path $destination $script) -Force
}

& (Join-Path $destination 'Install-Hooks.ps1') -ExecutablePath $installedExe -HooksPath $HooksPath -SkipShortcut:$SkipShortcut
if (-not $NoStart) { Start-Process -FilePath $installedExe }
Write-Output ('Installed Secretary Overlay to ' + $destination)
Write-Output 'In Codex Settings > Hook, trust the 12 new Secretary pet hooks once to receive task notifications.'
