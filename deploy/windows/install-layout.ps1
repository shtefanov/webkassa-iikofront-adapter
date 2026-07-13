param(
    [string]$Root = "$env:ProgramData\WebkassaIikoFrontAdapter"
)

$ErrorActionPreference = "Stop"

$directories = @(
    $Root,
    (Join-Path $Root "config"),
    (Join-Path $Root "secrets"),
    (Join-Path $Root "data"),
    (Join-Path $Root "logs"),
    (Join-Path $Root "support-bundles")
)

foreach ($directory in $directories) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

Write-Host "Webkassa iikoFront adapter layout prepared under $Root"
Write-Host "Copy config to: $(Join-Path $Root 'config\webkassa-adapter.config.json')"
Write-Host "Protected secrets directory: $(Join-Path $Root 'secrets')"
