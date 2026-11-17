param(
    [string]$ExecutablePath = (Join-Path $PSScriptRoot 'app\SecretaryOverlay.exe'),
    [string]$HooksPath = '',
    [switch]$SkipQuit
)
$ErrorActionPreference = 'Stop'
$petExe = [IO.Path]::GetFullPath($ExecutablePath)
$petCodexHome = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE '.codex' }
$petPath = if ($HooksPath) { [IO.Path]::GetFullPath($HooksPath) } else { Join-Path $petCodexHome 'hooks.json' }
function Test-SecretaryHook($handler) {
    $candidate = [string]$handler.commandWindows
    if (-not $candidate) { $candidate = [string]$handler.command }
    return ($handler.statusMessage -eq 'Secretary pet' -and $candidate -match 'SecretaryOverlay\.exe"?\s+--hook\s*$')
}
if (Test-Path -LiteralPath $petPath) {
    $petConfig = Get-Content -LiteralPath $petPath -Raw | ConvertFrom-Json
    $petBackupDir = if ($HooksPath) { Join-Path ([IO.Path]::GetDirectoryName($petPath)) 'backups' }
        else { Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'SecretaryOverlay\hook-backups' }
    New-Item -ItemType Directory -Path $petBackupDir -Force | Out-Null
    Copy-Item -LiteralPath $petPath -Destination (Join-Path $petBackupDir ('before-disconnect-' + (Get-Date -Format 'yyyyMMdd-HHmmss-ffff') + '.json'))
    if ($petConfig.hooks) {
        foreach ($petProperty in @($petConfig.hooks.PSObject.Properties)) {
            $petGroups = @()
            foreach ($petGroup in @($petProperty.Value)) {
                $petGroup.hooks = @($petGroup.hooks | Where-Object { -not (Test-SecretaryHook $_) })
                if ($petGroup.hooks.Count -gt 0) { $petGroups += $petGroup }
            }
            $petProperty.Value = $petGroups
        }
    }
    $petTemp = $petPath + '.secretary.tmp'
    [IO.File]::WriteAllText($petTemp, ($petConfig | ConvertTo-Json -Depth 30), [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $petTemp -Destination $petPath -Force
}
if (-not $SkipQuit -and (Test-Path -LiteralPath $petExe)) {
    Start-Process -FilePath $petExe -ArgumentList '--quit' -WindowStyle Hidden
}
Write-Output 'Secretary hooks disconnected; other hooks and original artwork preserved.'
