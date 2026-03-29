$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$stopScript = Join-Path $PSScriptRoot 'stop-local-core.ps1'
$startScript = Join-Path $PSScriptRoot 'start-local-core.ps1'

& $stopScript
Start-Sleep -Seconds 2
& $startScript
