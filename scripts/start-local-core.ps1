param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot

$services = @(
    @{
        Name = 'Identity'
        Project = Join-Path $root 'src\Services\Identity\FreshFarm.Identity.API\FreshFarm.Identity.Api.csproj'
        Exe = Join-Path $root 'src\Services\Identity\FreshFarm.Identity.API\bin\Release\net8.0\FreshFarm.Identity.Api.exe'
        Urls = 'https://localhost:7140;http://localhost:5140'
        Health = 'https://localhost:7140/health'
        HttpsPort = 7140
        ProcessName = 'FreshFarm.Identity.Api'
    },
    @{
        Name = 'Catalog'
        Project = Join-Path $root 'src\Services\Catalog\FreshFarm.Catalog.Api\FreshFarm.Catalog.Api.csproj'
        Exe = Join-Path $root 'src\Services\Catalog\FreshFarm.Catalog.Api\bin\Release\net8.0\FreshFarm.Catalog.Api.exe'
        Urls = 'https://localhost:7245;http://localhost:5102'
        Health = 'https://localhost:7245/health'
        HttpsPort = 7245
        ProcessName = 'FreshFarm.Catalog.Api'
    },
    @{
        Name = 'Ordering'
        Project = Join-Path $root 'src\Services\Ordering\FreshFarm.Ordering.Api\FreshFarm.Ordering.Api.csproj'
        Exe = Join-Path $root 'src\Services\Ordering\FreshFarm.Ordering.Api\bin\Release\net8.0\FreshFarm.Ordering.Api.exe'
        Urls = 'https://localhost:7018;http://localhost:5018'
        Health = 'https://localhost:7018/health'
        HttpsPort = 7018
        ProcessName = 'FreshFarm.Ordering.Api'
    },
    @{
        Name = 'BFF'
        Project = Join-Path $root 'src\Web\FreshFarm.Web.Bff\FreshFarm.Web.Bff.csproj'
        Exe = Join-Path $root 'src\Web\FreshFarm.Web.Bff\bin\Release\net8.0\FreshFarm.Web.Bff.exe'
        Urls = 'https://localhost:7085;http://localhost:5100'
        Health = 'https://localhost:7085/health'
        HttpsPort = 7085
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

function Get-ListenerProcess([int]$port) {
    $connection = Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -eq $connection) {
        return $null
    }

    return Get-CimInstance Win32_Process -Filter "ProcessId = $($connection.OwningProcess)" -ErrorAction SilentlyContinue
}

function Test-IsExpectedServiceProcess($service, $process) {
    if ($null -eq $process) {
        return $false
    }

    $expectedExe = [System.IO.Path]::GetFullPath($service.Exe)
    if (-not [string]::IsNullOrWhiteSpace($process.ExecutablePath) -and
        [string]::Equals($process.ExecutablePath, $expectedExe, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    $expectedDll = [System.IO.Path]::ChangeExtension($expectedExe, '.dll')
    return -not [string]::IsNullOrWhiteSpace($process.CommandLine) -and
        $process.CommandLine.Contains($expectedDll, [System.StringComparison]::OrdinalIgnoreCase)
}

if (-not $SkipBuild) {
    foreach ($service in $services) {
        Write-Host "$($service.Name): building current source in Release mode..."
        & dotnet build $service.Project -c Release --nologo
        if ($LASTEXITCODE -ne 0) {
            throw "$($service.Name): Release build failed. Local services were not started."
        }
    }
}

foreach ($service in $services) {
    if (-not (Test-Path $service.Exe)) {
        Write-Warning "$($service.Name): missing binary at $($service.Exe). Build Release first."
        continue
    }

    $listenerProcess = Get-ListenerProcess $service.HttpsPort
    if ($null -ne $listenerProcess) {
        if (-not (Test-IsExpectedServiceProcess $service $listenerProcess)) {
            throw "$($service.Name): port $($service.HttpsPort) is occupied by unexpected PID $($listenerProcess.ProcessId): $($listenerProcess.CommandLine)"
        }

        if (Test-Health $service.Health) {
            Write-Host "$($service.Name): current Release binary is already healthy at $($service.Health)"
            continue
        }

        throw "$($service.Name): expected process PID $($listenerProcess.ProcessId) owns port $($service.HttpsPort) but its health check failed."
    }

    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = $service.Urls
    $startedProcess = Start-Process -FilePath $service.Exe -WorkingDirectory (Split-Path $service.Exe) -WindowStyle Hidden -PassThru

    if (Wait-Health $service.Health) {
        Write-Host "$($service.Name): started and healthy at $($service.Health)"
        continue
    }

    if ($null -ne $startedProcess -and -not $startedProcess.HasExited) {
        Write-Warning "$($service.Name): process started but health check still failed. PID=$($startedProcess.Id)"
    }
    else {
        Write-Warning "$($service.Name): failed to start."
    }
}
