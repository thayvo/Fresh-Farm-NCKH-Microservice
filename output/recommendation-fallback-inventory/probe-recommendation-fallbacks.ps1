$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$workspace = 'D:\NCKH\DOAN\NCKH-FRESH-FARM'
$outputDir = Join-Path $workspace 'output\recommendation-fallback-inventory'
$outputPath = Join-Path $outputDir 'latest.json'
$sqlServer = 'LAPTOP-D3S57BE5\DEVSQL'

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

function Invoke-JsonEndpoint {
    param(
        [string]$Url,
        [Microsoft.PowerShell.Commands.WebRequestSession]$Session
    )

    $response = Invoke-WebRequest -WebSession $Session -UseBasicParsing $Url -TimeoutSec 20
    $json = $response.Content | ConvertFrom-Json

    return [ordered]@{
        url = $Url
        statusCode = [int]$response.StatusCode
        algorithm = $json.algorithm
        rankingAlgorithm = $json.rankingAlgorithm
        signalSource = $json.signalSource
        rankingSignalSource = $json.rankingSignalSource
        fallbackReason = $json.fallbackReason
        rankingFallbackReason = $json.rankingFallbackReason
        preferenceSignalSource = $json.preferenceSignalSource
        collaborativeSignalSource = $json.collaborativeSignalSource
        preferenceFallbackReason = $json.preferenceFallbackReason
        collaborativeFallbackReason = $json.collaborativeFallbackReason
        contentSignalSource = $json.contentSignalSource
        signalBreakdown = $json.signalBreakdown
        itemCount = @($json.items).Count
        totalCount = $json.totalCount
        firstProductId = if (@($json.items).Count -gt 0) { $json.items[0].productId } else { $null }
        firstReason = if (@($json.items).Count -gt 0) { $json.items[0].recommendationReason } else { $null }
        generatedAtUtc = $json.generatedAtUtc
    }
}

$coldHomeSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$defaultSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession

$recommendationProductAffinityRows = [int]((sqlcmd -S $sqlServer -E -W -h -1 -Q "SELECT COUNT(*) FROM FreshFarmOrderingDB.dbo.RecommendationProductAffinity;" | Where-Object { $_ -and $_.Trim().Length -gt 0 } | Select-Object -First 1).Trim())

$recommendationSearchKeywordAffinityRows = [int]((sqlcmd -S $sqlServer -E -W -h -1 -Q "SELECT COUNT(*) FROM FreshFarmOrderingDB.dbo.RecommendationSearchKeywordAffinity;" | Where-Object { $_ -and $_.Trim().Length -gt 0 } | Select-Object -First 1).Trim())

$recommendationHomePreferenceSeedRows = [int]((sqlcmd -S $sqlServer -E -W -h -1 -Q "SELECT COUNT(*) FROM FreshFarmOrderingDB.dbo.RecommendationHomePreferenceSeed;" | Where-Object { $_ -and $_.Trim().Length -gt 0 } | Select-Object -First 1).Trim())

$recommendationHomeCollaborativeCandidateRows = [int]((sqlcmd -S $sqlServer -E -W -h -1 -Q "SELECT COUNT(*) FROM FreshFarmOrderingDB.dbo.RecommendationHomeCollaborativeCandidate;" | Where-Object { $_ -and $_.Trim().Length -gt 0 } | Select-Object -First 1).Trim())

$materializedSearchKeywords = @(
sqlcmd -S $sqlServer -E -W -h -1 -Q "SELECT [Keyword] FROM FreshFarmOrderingDB.dbo.RecommendationSearchKeywordAffinity GROUP BY [Keyword] ORDER BY [Keyword];" |
    Where-Object { $_ -and $_.Trim().Length -gt 0 -and $_.Trim() -notmatch '^\(\d+ rows affected\)$' } |
    ForEach-Object { $_.Trim() }
)

$report = [ordered]@{
    generatedAtUtc = [DateTime]::UtcNow.ToString('o')
    endpoints = [ordered]@{
        homeColdStart = Invoke-JsonEndpoint -Url 'https://localhost:7085/bff/recommendations/home?limit=4' -Session $coldHomeSession
        searchKnownKeyword = Invoke-JsonEndpoint -Url 'https://localhost:7085/bff/product-search?name=rau&page=1&pageSize=5' -Session $defaultSession
        searchColdKeyword = Invoke-JsonEndpoint -Url 'https://localhost:7085/bff/product-search?name=mini&page=1&pageSize=5' -Session $defaultSession
        similarMaterialized = Invoke-JsonEndpoint -Url 'https://localhost:7085/bff/recommendations/products/104/similar?limit=4' -Session $defaultSession
        similarContentFallback = Invoke-JsonEndpoint -Url 'https://localhost:7085/bff/recommendations/products/105/similar?limit=4' -Session $defaultSession
    }
    database = [ordered]@{
        recommendationProductAffinityRows = $recommendationProductAffinityRows
        recommendationSearchKeywordAffinityRows = $recommendationSearchKeywordAffinityRows
        recommendationHomePreferenceSeedRows = $recommendationHomePreferenceSeedRows
        recommendationHomeCollaborativeCandidateRows = $recommendationHomeCollaborativeCandidateRows
        materializedSearchKeywords = $materializedSearchKeywords
    }
}

$report | ConvertTo-Json -Depth 10 | Set-Content -Path $outputPath -Encoding UTF8
Write-Output $outputPath
