$src = Join-Path (Get-Location) 'finalBaoMat_source.md'
$dst = Join-Path (Get-Location) 'finalBaoMat.docx'
$tmpHtml = Join-Path (Get-Location) 'finalBaoMat_tmp.html'

if (-not (Test-Path $src)) {
    throw "Khong tim thay file nguon: $src"
}

function Encode-Html {
    param([string]$Text)
    return [System.Net.WebUtility]::HtmlEncode($Text)
}

$html = New-Object System.Collections.Generic.List[string]
$html.Add('<!DOCTYPE html>')
$html.Add('<html lang="vi">')
$html.Add('<head>')
$html.Add('<meta charset="utf-8">')
$html.Add('<title>finalBaoMat</title>')
$html.Add('<style>')
$html.Add('body { font-family: "Times New Roman", serif; font-size: 12pt; line-height: 1.35; margin: 24px; }')
$html.Add('h1 { font-size: 18pt; text-align: center; margin: 0 0 12pt 0; }')
$html.Add('h2 { font-size: 16pt; margin: 14pt 0 6pt 0; }')
$html.Add('h3 { font-size: 14pt; margin: 10pt 0 4pt 0; }')
$html.Add('h4 { font-size: 13pt; margin: 8pt 0 4pt 0; font-style: italic; }')
$html.Add('p { margin: 0 0 6pt 0; }')
$html.Add('ul, ol { margin: 0 0 6pt 18pt; }')
$html.Add('li { margin: 0 0 3pt 0; }')
$html.Add('</style>')
$html.Add('</head>')
$html.Add('<body>')

$listMode = ''

function Close-List {
    param(
        [System.Collections.Generic.List[string]]$Html,
        [ref]$ListMode
    )

    if ($ListMode.Value -eq 'ul') {
        $Html.Add('</ul>')
    }
    elseif ($ListMode.Value -eq 'ol') {
        $Html.Add('</ol>')
    }

    $ListMode.Value = ''
}

foreach ($line in Get-Content -Path $src -Encoding UTF8) {
    $raw = $line.TrimEnd()
    $clean = $raw -replace '`', ''

    if ([string]::IsNullOrWhiteSpace($clean)) {
        Close-List -Html $html -ListMode ([ref]$listMode)
        $html.Add('<p>&nbsp;</p>')
        continue
    }

    if ($clean -match '^#\s+(.+)$') {
        Close-List -Html $html -ListMode ([ref]$listMode)
        $html.Add("<h1>$(Encode-Html $Matches[1])</h1>")
        continue
    }

    if ($clean -match '^##\s+(.+)$') {
        Close-List -Html $html -ListMode ([ref]$listMode)
        $html.Add("<h2>$(Encode-Html $Matches[1])</h2>")
        continue
    }

    if ($clean -match '^###\s+(.+)$') {
        Close-List -Html $html -ListMode ([ref]$listMode)
        $html.Add("<h3>$(Encode-Html $Matches[1])</h3>")
        continue
    }

    if ($clean -match '^####\s+(.+)$') {
        Close-List -Html $html -ListMode ([ref]$listMode)
        $html.Add("<h4>$(Encode-Html $Matches[1])</h4>")
        continue
    }

    if ($clean -match '^\s*-\s+(.+)$') {
        if ($listMode -ne 'ul') {
            Close-List -Html $html -ListMode ([ref]$listMode)
            $html.Add('<ul>')
            $listMode = 'ul'
        }

        $html.Add("<li>$(Encode-Html $Matches[1])</li>")
        continue
    }

    if ($clean -match '^\s*\d+\.\s+(.+)$') {
        if ($listMode -ne 'ol') {
            Close-List -Html $html -ListMode ([ref]$listMode)
            $html.Add('<ol>')
            $listMode = 'ol'
        }

        $html.Add("<li>$(Encode-Html $Matches[1])</li>")
        continue
    }

    Close-List -Html $html -ListMode ([ref]$listMode)
    $html.Add("<p>$(Encode-Html $clean)</p>")
}

Close-List -Html $html -ListMode ([ref]$listMode)
$html.Add('</body>')
$html.Add('</html>')

[System.IO.File]::WriteAllLines($tmpHtml, $html, [System.Text.Encoding]::UTF8)

$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0

try {
    if (Test-Path $dst) {
        Remove-Item $dst -Force
    }

    $doc = $word.Documents.Open($tmpHtml)
    try {
        $doc.SaveAs2($dst, 16)
    }
    finally {
        if ($doc -ne $null) {
            $doc.Close()
        }
    }
}
finally {
    $word.Quit()
}

if (Test-Path $tmpHtml) {
    Remove-Item $tmpHtml -Force
}

Get-Item $dst | Select-Object FullName, Length, LastWriteTime
