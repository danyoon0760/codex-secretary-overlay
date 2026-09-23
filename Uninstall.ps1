param(
    [string]$InstallDirectory = $PSScriptRoot,
    [string]$HooksPath = ''
)
$ErrorActionPreference = 'Stop'
$destination = [IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\')
$allowedParent = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs')).TrimEnd('\')
if (-not $destination.StartsWith($allowedParent + '\', [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($destination) -ne 'SecretaryOverlay') {
    throw 'Uninstall only removes the SecretaryOverlay folder inside the user Programs directory.'
}
if (-not (Test-Path -LiteralPath $destination -PathType Container)) { throw 'The installation folder does not exist.' }
if ((Get-Item -LiteralPath $destination).Attributes -band [IO.FileAttributes]::ReparsePoint) {
    throw 'Uninstall will not remove a linked installation folder.'
}
$installedExe = Join-Path $destination 'SecretaryOverlay.exe'
& (Join-Path $destination 'Disconnect-Hooks.ps1') -ExecutablePath $installedExe -HooksPath $HooksPath -SkipQuit
if (Test-Path -LiteralPath $installedExe -PathType Leaf) {
    $main = @(Get-CimInstance Win32_Process -Filter "name = 'SecretaryOverlay.exe'" |
        Where-Object { $_.ExecutablePath -eq $installedExe -and $_.CommandLine -notmatch '--hook' })
    if ($main.Count -gt 0) { Start-Process -FilePath $installedExe -ArgumentList '--quit' -Wait -WindowStyle Hidden }
    $deadline = (Get-Date).AddSeconds(15)
    do {
        $running = @(Get-CimInstance Win32_Process -Filter "name = 'SecretaryOverlay.exe'" |
            Where-Object { $_.ExecutablePath -eq $installedExe })
        if ($running.Count -eq 0) { break }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    if ($running.Count -gt 0) { throw 'Close the running Secretary Overlay and run the uninstaller again.' }
}

$shortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) '비서 펫.lnk'
if (Test-Path -LiteralPath $shortcut) {
    $shell = New-Object -ComObject WScript.Shell
    if ([IO.Path]::GetFullPath($shell.CreateShortcut($shortcut).TargetPath) -eq $installedExe) {
        Remove-Item -LiteralPath $shortcut -Force
    }
}
Remove-Item -LiteralPath $destination -Recurse -Force
Write-Output 'Secretary Overlay was removed. Personal settings in LocalAppData were kept.'
