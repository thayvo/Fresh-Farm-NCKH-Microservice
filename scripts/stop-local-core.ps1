$ErrorActionPreference = 'Stop'

$services = @(
    @{ Name = 'Identity'; ProcessName = 'FreshFarm.Identity.Api'; AssemblyName = 'FreshFarm.Identity.Api.dll'; Ports = @(7140, 5140) },
    @{ Name = 'Catalog'; ProcessName = 'FreshFarm.Catalog.Api'; AssemblyName = 'FreshFarm.Catalog.Api.dll'; Ports = @(7245, 5102) },
    @{ Name = 'Ordering'; ProcessName = 'FreshFarm.Ordering.Api'; AssemblyName = 'FreshFarm.Ordering.Api.dll'; Ports = @(7018, 5018) },
    @{ Name = 'BFF'; ProcessName = 'FreshFarm.Web.Bff'; AssemblyName = 'FreshFarm.Web.Bff.dll'; Ports = @(7085, 5100) }
)

foreach ($service in $services) {
    $candidateIds = [System.Collections.Generic.HashSet[int]]::new()

    foreach ($process in (Get-Process -Name $service.ProcessName -ErrorAction SilentlyContinue)) {
        $null = $candidateIds.Add($process.Id)
    }

    foreach ($port in $service.Ports) {
        foreach ($connection in (Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue)) {
            $process = Get-CimInstance Win32_Process -Filter "ProcessId = $($connection.OwningProcess)" -ErrorAction SilentlyContinue
            if ($null -ne $process -and
                -not [string]::IsNullOrWhiteSpace($process.CommandLine) -and
                $process.CommandLine.Contains($service.AssemblyName, [System.StringComparison]::OrdinalIgnoreCase)) {
                $null = $candidateIds.Add([int]$process.ProcessId)
            }
        }
    }

    if ($candidateIds.Count -eq 0) {
        Write-Host "$($service.Name): not running"
        continue
    }

    foreach ($processId in $candidateIds) {
        Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
        Write-Host "$($service.Name): stopped PID $processId"
    }
}
