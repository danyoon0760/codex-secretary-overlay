$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetFullPath([IO.Path]::GetTempPath())) ('Secretary Overlay hook test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
try {
    $fake = Join-Path $testRoot 'SecretaryOverlay.exe'
    $hooks = Join-Path $testRoot 'hooks.json'
    [IO.File]::WriteAllBytes($fake, [byte[]]@())
    $config = [pscustomobject]@{ hooks = [pscustomobject]@{
        Stop = @([pscustomobject]@{ hooks = @(
            [pscustomobject]@{type='command';command='echo other';statusMessage='Other hook'},
            [pscustomobject]@{type='command';command='C:\old location\SecretaryOverlay.exe --hook';statusMessage='Secretary pet'}
        ) })
    } }
    $config | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $hooks
    & (Join-Path $PSScriptRoot 'Install-Hooks.ps1') -ExecutablePath $fake -HooksPath $hooks -SkipShortcut | Out-Null
    $installed = Get-Content -Raw -LiteralPath $hooks | ConvertFrom-Json
    $ours = @($installed.hooks.Stop | ForEach-Object { $_.hooks } | Where-Object { $_.statusMessage -eq 'Secretary pet' })
    $others = @($installed.hooks.Stop | ForEach-Object { $_.hooks } | Where-Object { $_.statusMessage -eq 'Other hook' })
    if ($ours.Count -ne 1 -or $others.Count -ne 1 -or $ours[0].commandWindows -ne ('"' + $fake + '" --hook')) {
        throw 'Whitespace paths or replacement of an old owned hook failed.'
    }
    & (Join-Path $PSScriptRoot 'Disconnect-Hooks.ps1') -ExecutablePath (Join-Path $testRoot 'moved\SecretaryOverlay.exe') -HooksPath $hooks -SkipQuit | Out-Null
    $disconnected = Get-Content -Raw -LiteralPath $hooks | ConvertFrom-Json
    $remaining = @($disconnected.hooks.Stop | ForEach-Object { $_.hooks })
    if ($remaining.Count -ne 1 -or $remaining[0].command -ne 'echo other') {
        throw 'Moved-app disconnect removed the wrong hook.'
    }
    Write-Output 'Hook scripts: whitespace path, old-hook replacement, and moved-app disconnect passed.'
}
finally {
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    $resolvedTest = [IO.Path]::GetFullPath($testRoot)
    $safeParent = $resolvedTest.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)
    $safeName = [IO.Path]::GetFileName($resolvedTest) -match '^Secretary Overlay hook test-[0-9a-f]{32}$'
    if (-not $safeParent -or -not $safeName) {
        throw 'Unsafe temporary test target.'
    }
    if (Test-Path -LiteralPath $resolvedTest) { Remove-Item -LiteralPath $resolvedTest -Recurse -Force }
}
