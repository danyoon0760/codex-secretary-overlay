param(
    [string]$ExecutablePath = (Join-Path $PSScriptRoot 'app\SecretaryOverlay.exe'),
    [string]$HooksPath = '',
    [switch]$SkipShortcut
)
$ErrorActionPreference = 'Stop'
$petExe = [IO.Path]::GetFullPath($ExecutablePath)
if (-not (Test-Path -LiteralPath $petExe -PathType Leaf)) { throw 'Build the application first.' }
if ($petExe -match '["\r\n%!]') { throw 'The hook executable path contains unsupported command characters.' }
$petCodexHome = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE '.codex' }
$petHooksPath = if ($HooksPath) { [IO.Path]::GetFullPath($HooksPath) } else { Join-Path $petCodexHome 'hooks.json' }
$petCommand = '"' + $petExe + '" --hook'
$petEvents = @('SessionStart','UserPromptSubmit','PreToolUse','PostToolUse','PermissionRequest','Stop','Interrupt','PreCompact','PostCompact','SubagentStart','SubagentStop','SessionEnd')
$petConfig = if (Test-Path -LiteralPath $petHooksPath) { Get-Content -Raw -LiteralPath $petHooksPath | ConvertFrom-Json } else { [pscustomobject]@{ hooks = [pscustomobject]@{} } }
if (-not $petConfig.PSObject.Properties['hooks']) { $petConfig | Add-Member NoteProperty hooks ([pscustomobject]@{}) }
if (Test-Path -LiteralPath $petHooksPath) {
    $petBackupDir = if ($HooksPath) { Join-Path ([IO.Path]::GetDirectoryName($petHooksPath)) 'backups' }
        else { Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'SecretaryOverlay\hook-backups' }
    New-Item -ItemType Directory -Path $petBackupDir -Force | Out-Null
    Copy-Item -LiteralPath $petHooksPath -Destination (Join-Path $petBackupDir ('hooks-' + (Get-Date -Format 'yyyyMMdd-HHmmss-ffff') + '.json'))
}
function Test-SecretaryHook($handler) {
    $candidate = [string]$handler.commandWindows
    if (-not $candidate) { $candidate = [string]$handler.command }
    return ($handler.statusMessage -eq 'Secretary pet' -and $candidate -match 'SecretaryOverlay\.exe"?\s+--hook\s*$')
}
foreach ($petEvent in $petEvents) {
    $petExisting = @()
    if ($petConfig.hooks.PSObject.Properties[$petEvent]) { $petExisting = @($petConfig.hooks.$petEvent) }
    $petGroups = @()
    foreach ($petGroup in $petExisting) {
        $petGroup.hooks = @($petGroup.hooks | Where-Object { -not (Test-SecretaryHook $_) })
        if ($petGroup.hooks.Count -gt 0) { $petGroups += $petGroup }
    }
    $petEntry = [pscustomobject]@{ hooks = @([pscustomobject]@{
        type = 'command'; command = $petCommand; commandWindows = $petCommand
        timeout = 3; async = ($petEvent -ne 'SessionEnd'); statusMessage = 'Secretary pet'
    }) }
    $petConfig.hooks | Add-Member -Force NoteProperty $petEvent @($petGroups + $petEntry)
}
$petTemp = $petHooksPath + '.secretary.tmp'
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($petHooksPath)) | Out-Null
[IO.File]::WriteAllText($petTemp, ($petConfig | ConvertTo-Json -Depth 30), [Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $petTemp -Destination $petHooksPath -Force
Write-Output ('Registered 12 event hooks: ' + $petHooksPath)
Write-Output 'Review and trust these hooks in Codex Settings > Hook. This script does not modify trust settings.'
if (-not $SkipShortcut) {
    $petShell = New-Object -ComObject WScript.Shell
    $petShortcut = $petShell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) '비서 펫.lnk'))
    $petShortcut.TargetPath = $petExe
    $petShortcut.WorkingDirectory = (Split-Path $petExe)
    $petShortcut.Description = 'Codex 후크 비서 오버레이'
    $petShortcut.Save()
}
