param(
    [string]$BffBaseUrl = 'https://localhost:7085'
)

$ErrorActionPreference = 'Stop'

function Invoke-RecommendationProbe {
    param(
        [string]$Name,
        [string]$Url
    )

    try {
        $response = Invoke-WebRequest -UseBasicParsing $Url
        $json = $response.Content | ConvertFrom-Json

        $itemIds = @()
        $sellerIds = @()
        if ($null -ne $json.items) {
            $itemIds = @($json.items | ForEach-Object { $_.productId })
            $sellerIds = @($json.items | ForEach-Object { $_.primarySellerId })
        }

        [pscustomobject]@{
            name = $Name
            url = $Url
            status = [int]$response.StatusCode
            algorithm = if ($null -ne $json.algorithm) { [string]$json.algorithm } elseif ($null -ne $json.rankingAlgorithm) { [string]$json.rankingAlgorithm } else { $null }
            signalSource = if ($null -ne $json.signalSource) { [string]$json.signalSource } elseif ($null -ne $json.rankingSignalSource) { [string]$json.rankingSignalSource } else { $null }
            fallbackReason = if ($null -ne $json.fallbackReason) { [string]$json.fallbackReason } elseif ($null -ne $json.rankingFallbackReason) { [string]$json.rankingFallbackReason } else { $null }
            contentSignalSource = if ($null -ne $json.contentSignalSource) { [string]$json.contentSignalSource } else { $null }
            signalBreakdown = if ($null -ne $json.signalBreakdown) { $json.signalBreakdown } else { $null }
            topItemIds = @($itemIds | Select-Object -First 5)
            topSellerIds = @($sellerIds | Select-Object -First 5)
            uniqueSellerCount = @($sellerIds | Where-Object { $null -ne $_ } | Select-Object -Unique).Count
            itemCount = if ($null -ne $json.items) { @($json.items).Count } elseif ($null -ne $json.relatedItems) { @($json.relatedItems).Count } else { 0 }
            capturedAtUtc = [DateTime]::UtcNow.ToString('o')
        }
    }
    catch {
        [pscustomobject]@{
            name = $Name
            url = $Url
            status = $null
            algorithm = $null
            signalSource = $null
            fallbackReason = $_.Exception.Message
            contentSignalSource = $null
            signalBreakdown = $null
            topItemIds = @()
            topSellerIds = @()
            uniqueSellerCount = 0
            itemCount = 0
            capturedAtUtc = [DateTime]::UtcNow.ToString('o')
        }
    }
}

$probes = @(
    @{ Name = 'home_cold_start'; Url = "$BffBaseUrl/bff/recommendations/home?limit=4" },
    @{ Name = 'search_hot_keyword_rau'; Url = "$BffBaseUrl/bff/product-search?name=rau&page=1&pageSize=5" },
    @{ Name = 'search_cold_keyword_mini'; Url = "$BffBaseUrl/bff/product-search?name=mini&page=1&pageSize=5" },
    @{ Name = 'similar_hybrid_seed_1'; Url = "$BffBaseUrl/bff/recommendations/products/1/similar?limit=4" },
    @{ Name = 'similar_content_seed_105'; Url = "$BffBaseUrl/bff/recommendations/products/105/similar?limit=4" }
)

$results = @($probes | ForEach-Object { Invoke-RecommendationProbe -Name $_.Name -Url $_.Url })
$payload = [pscustomobject]@{
    generatedAtUtc = [DateTime]::UtcNow.ToString('o')
    bffBaseUrl = $BffBaseUrl
    results = $results
}

$outputPath = Join-Path $PSScriptRoot 'latest.json'
$summaryPath = Join-Path $PSScriptRoot 'latest-summary.md'
$payload | ConvertTo-Json -Depth 8 | Set-Content -Path $outputPath -Encoding UTF8

$summaryLines = @(
    '# Recommendation Evaluation Summary',
    '',
    "- Generated at (UTC): $($payload.generatedAtUtc)",
    "- BFF base URL: $BffBaseUrl",
    ''
)

foreach ($result in $results) {
    $topIds = if ($result.topItemIds.Count -gt 0) { ($result.topItemIds -join ', ') } else { 'none' }
    $topSellerIds = if ($result.topSellerIds.Count -gt 0) { ($result.topSellerIds -join ', ') } else { 'none' }
    $summaryLines += "## $($result.name)"
    $summaryLines += ''
    $summaryLines += "- Status: $($result.status)"
    $summaryLines += "- Algorithm: $($result.algorithm)"
    $summaryLines += "- Signal source: $($result.signalSource)"
    $summaryLines += "- Content signal source: $($result.contentSignalSource)"
    $summaryLines += "- Fallback reason: $($result.fallbackReason)"
    $summaryLines += "- Item count: $($result.itemCount)"
    $summaryLines += "- Top item ids: $topIds"
    $summaryLines += "- Top seller ids: $topSellerIds"
    $summaryLines += "- Unique seller count: $($result.uniqueSellerCount)"
    if ($null -ne $result.signalBreakdown) {
        $summaryLines += "- Signal breakdown:"
        foreach ($property in $result.signalBreakdown.PSObject.Properties) {
            $summaryLines += "  - $($property.Name): $($property.Value)"
        }
    }
    $summaryLines += ''
}

$summaryLines | Set-Content -Path $summaryPath -Encoding UTF8
$payload | ConvertTo-Json -Depth 8
