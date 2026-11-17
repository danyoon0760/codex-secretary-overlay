# Runs the app's built-in self-test and fails with its error log when a check fails.
# The app is a GUI executable, so wait for it explicitly to read the exit code.
# Keep this file ASCII-only: Windows PowerShell 5.1 misreads BOM-less UTF-8.
param(
    [string]$ExecutablePath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'bin\Release\net10.0-windows10.0.19041.0\SecretaryOverlay.exe')
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $ExecutablePath -PathType Leaf)) { throw "Build first: $ExecutablePath not found." }

$data = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'SecretaryOverlay'
$errors = Join-Path $data 'errors.log'
$before = if (Test-Path -LiteralPath $errors) { (Get-Item -LiteralPath $errors).Length } else { 0 }

$process = Start-Process -FilePath $ExecutablePath -ArgumentList '--self-test' -Wait -PassThru
if ($process.ExitCode -ne 0) {
    if (Test-Path -LiteralPath $errors) {
        $stream = [IO.File]::OpenRead($errors)
        try {
            [void]$stream.Seek($before, [IO.SeekOrigin]::Begin)
            Write-Output (New-Object IO.StreamReader($stream)).ReadToEnd()
        }
        finally { $stream.Dispose() }
    }
    throw "Self-test failed with exit code $($process.ExitCode)."
}
$report = Join-Path $data 'self-test.json'
$count = if (Test-Path -LiteralPath $report) { @((Get-Content -Raw -LiteralPath $report | ConvertFrom-Json).checks).Count } else { 0 }
Write-Output "Self-test passed ($count checks)."
