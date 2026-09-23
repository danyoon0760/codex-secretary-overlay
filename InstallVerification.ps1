$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('SecretaryOverlay-install-test-' + [guid]::NewGuid().ToString('N'))
$package = Join-Path $root 'release [1]'
$sourceApp = Join-Path $package 'app'
$destination = Join-Path $root '설치 사용자 [1]\SecretaryOverlay'
$hooksPath = Join-Path $root 'Codex 설정 [1]\hooks.json'

try {
    New-Item -ItemType Directory -Path (Join-Path $sourceApp 'assets'), (Split-Path -Parent $hooksPath) -Force | Out-Null
    foreach ($name in @('Install.ps1', 'Install-Hooks.ps1', 'Disconnect-Hooks.ps1', 'Uninstall.ps1', 'Uninstall.cmd')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $package $name)
    }
    [IO.File]::WriteAllText((Join-Path $sourceApp 'SecretaryOverlay.exe'), 'installer copy fixture')
    [IO.File]::WriteAllText((Join-Path $sourceApp 'assets\idle.png'), 'asset fixture')
    [IO.File]::WriteAllText($hooksPath, '{"hooks":{"SessionStart":[{"hooks":[{"type":"command","command":"other-app","statusMessage":"Other hook"}]}]}}')

    & (Join-Path $package 'Install.ps1') -InstallDirectory $destination -HooksPath $hooksPath -SkipShortcut -NoStart | Out-Null
    foreach ($name in @('SecretaryOverlay.exe', 'assets\idle.png', 'Uninstall.ps1', 'Uninstall.cmd')) {
        if (-not (Test-Path -LiteralPath (Join-Path $destination $name) -PathType Leaf)) { throw "Installer did not copy $name" }
    }
    $config = Get-Content -LiteralPath $hooksPath -Raw | ConvertFrom-Json
    $handlers = @($config.hooks.PSObject.Properties | ForEach-Object { $_.Value } | ForEach-Object { $_.hooks })
    if (@($handlers | Where-Object statusMessage -eq 'Secretary pet').Count -ne 12) { throw 'Installer did not register 12 hooks.' }
    if (@($handlers | Where-Object statusMessage -eq 'Other hook').Count -ne 1) { throw 'Installer changed an unrelated hook.' }

    & (Join-Path $package 'Disconnect-Hooks.ps1') -ExecutablePath (Join-Path $destination 'SecretaryOverlay.exe') -HooksPath $hooksPath -SkipQuit | Out-Null
    $config = Get-Content -LiteralPath $hooksPath -Raw | ConvertFrom-Json
    $handlers = @($config.hooks.PSObject.Properties | ForEach-Object { $_.Value } | ForEach-Object { $_.hooks })
    if (@($handlers | Where-Object statusMessage -eq 'Secretary pet').Count -ne 0) { throw 'Disconnect left owned hooks behind.' }
    if (@($handlers | Where-Object statusMessage -eq 'Other hook').Count -ne 1) { throw 'Disconnect changed an unrelated hook.' }

    Write-Output 'Release installer: bracket, spaces, Korean path, assets, and hook preservation passed.'
}
finally {
    $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
    $resolved = [IO.Path]::GetFullPath($root).TrimEnd('\')
    if ($resolved.StartsWith($temp + '\', [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolved)) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
