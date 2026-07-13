$ErrorActionPreference = "Stop"

$termPath = "C:\OpenClaw\work\webkassa\scripts\backoffice-search-term.txt"
$term = (Get-Content -Path $termPath -Encoding UTF8 -Raw).Trim()
if ([string]::IsNullOrWhiteSpace($term)) {
    throw "Empty BackOffice search term in $termPath"
}

$safeName = ($term -replace '[^\p{L}\p{Nd}]+', '-').Trim('-')
if ([string]::IsNullOrWhiteSpace($safeName)) {
    $safeName = "term"
}

& "C:\OpenClaw\work\webkassa\scripts\launch-backoffice-search-menu.ps1" `
    -Term $term `
    -ScreenshotPath "C:\OpenClaw\logs\bo-search-$safeName.png" `
    -UiPath "C:\OpenClaw\logs\bo-search-$safeName-ui.txt"
