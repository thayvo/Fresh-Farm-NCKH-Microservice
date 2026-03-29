$ErrorActionPreference = 'Stop'

$processNames = @(
    'FreshFarm.Identity.Api',
    'FreshFarm.Catalog.Api',
    'FreshFarm.Ordering.Api',
    'FreshFarm.Web.Bff'
)

foreach ($processName in $processNames) {
    $processes = Get-Process -Name $processName -ErrorAction SilentlyContinue
    if ($null -eq $processes) {
        Write-Host "${processName}: not running"
        continue
    }

    foreach ($process in $processes) {
        Stop-Process -Id $process.Id -Force
        Write-Host "${processName}: stopped PID $($process.Id)"
    }
}
