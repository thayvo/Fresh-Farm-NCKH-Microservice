$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot

$services = @(
    @{
        Name = 'Identity'
        Exe = Join-Path $root 'src\Services\Identity\FreshFarm.Identity.API\bin\Release\net8.0\FreshFarm.Identity.Api.exe'
        Urls = 'https://localhost:7140;http://localhost:5140'
        Health = 'https://localhost:7140/health'
        ProcessName = 'FreshFarm.Identity.Api'
    },
    @{
        Name = 'Catalog'
        Exe = Join-Path $root 'src\Services\Catalog\FreshFarm.Catalog.Api\bin\Release\net8.0\FreshFarm.Catalog.Api.exe'
        Urls = 'https://localhost:7245;http://localhost:5102'
        Health = 'https://localhost:7245/health'
        ProcessName = 'FreshFarm.Catalog.Api'
    },
    @{
        Name = 'Ordering'
        Exe = Join-Path $root 'src\Services\Ordering\FreshFarm.Ordering.Api\bin\Release\net8.0\FreshFarm.Ordering.Api.exe'
        Urls = 'https://localhost:7018;http://localhost:5018'
        Health = 'https://localhost:7018/health'
        ProcessName = 'FreshFarm.Ordering.Api'
    },
    @{
        Name = 'BFF'
        Exe = Join-Path $root 'src\Web\FreshFarm.Web.Bff\bin\Release\net8.0\FreshFarm.Web.Bff.exe'
        Urls = 'https://localhost:7085;http://localhost:5100'
        Health = 'https://localhost:7085/health'
        ProcessName = 'FreshFarm.Web.Bff'
    }
)

function Test-Health([string]$url) {
    try {
        $null = Invoke-WebRequest -UseBasicParsing $url -TimeoutSec 5
        return $true
    }
    catch {
        return $false
    }
}

function Wait-Health([string]$url, [int]$timeoutSeconds = 30) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Health $url) {
            return $true
        }

        Start-Sleep -Seconds 1
    }

    return $false
}

foreach ($service in $services) {
    if (-not (Test-Path $service.Exe)) {
        Write-Warning "$($service.Name): missing binary at $($service.Exe). Build Release first."
        continue
    }

    if (Test-Health $service.Health) {
        Write-Host "$($service.Name): already healthy at $($service.Health)"
        continue
    }

    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = $service.Urls
    Start-Process -FilePath $service.Exe -WorkingDirectory (Split-Path $service.Exe)

    if (Wait-Health $service.Health) {
        Write-Host "$($service.Name): started and healthy at $($service.Health)"
        continue
    }

    $processInfo = Get-Process -Name $service.ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1 Id, ProcessName
    if ($null -ne $processInfo) {
        Write-Warning "$($service.Name): process started but health check still failed. PID=$($processInfo.Id)"
    }
    else {
        Write-Warning "$($service.Name): failed to start."
    }
}
