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
    [string]$MinIikoFrontVersion = "9.5",
    [string]$MinIikoFrontApiVersion = "V9",
    [string]$Signature = ""
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

$manifest = [ordered]@{
    channel = $Channel
    version = $Version
    packageUrl = $PackageUrl
    sha256 = $sha256
    signature = $Signature
    minIikoFrontVersion = $MinIikoFrontVersion
    minIikoFrontApiVersion = $MinIikoFrontApiVersion
    releaseNotesUrl = $ReleaseNotesUrl
    publishedAt = (Get-Date -Format "dd-MM-yyyy")
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
