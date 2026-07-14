param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,
    [Parameter(Mandatory = $true)]
    [ValidateSet("beta", "stable")]
    [string]$Channel,
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [Parameter(Mandatory = $true)]
    [string]$PackageUrl,
    [Parameter(Mandatory = $true)]
    [string]$ReleaseNotesUrl,
    [string]$OutputPath = "",
    [string]$Project = "webkassa",
    [string]$MinIikoFrontVersion = "9.5",
    [string]$MinIikoFrontApiVersion = "V9",
    [string[]]$SupportedIikoFrontApiVersions = @("V9")
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $PackagePath)) {
    throw "Package was not found: $PackagePath"
}

if (-not $PackageUrl.StartsWith("https://", [StringComparison]::OrdinalIgnoreCase)) {
    throw "PackageUrl must use HTTPS."
}

if (-not $ReleaseNotesUrl.StartsWith("https://", [StringComparison]::OrdinalIgnoreCase)) {
    throw "ReleaseNotesUrl must use HTTPS."
}

$sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $PackagePath).Hash.ToLowerInvariant()
$packageItem = Get-Item -LiteralPath $PackagePath

$manifest = [ordered]@{
    schemaVersion = 1
    project = $Project
    channel = $Channel
    version = $Version
    packageUrl = $PackageUrl
    packageFileName = $packageItem.Name
    packageSize = $packageItem.Length
    sha256 = $sha256
    minIikoFrontVersion = $MinIikoFrontVersion
    minIikoFrontApiVersion = $MinIikoFrontApiVersion
    supportedIikoFrontApiVersions = $SupportedIikoFrontApiVersions
    releaseNotesUrl = $ReleaseNotesUrl
    publishedAt = (Get-Date).ToString("o")
}

$json = $manifest | ConvertTo-Json -Depth 5

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $json
} else {
    $parent = Split-Path -Parent $OutputPath
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }

    Set-Content -Encoding UTF8 -LiteralPath $OutputPath -Value $json
    Get-Item -LiteralPath $OutputPath
}
