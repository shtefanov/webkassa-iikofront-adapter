param(
    [string]$ManifestUrl = "",
    [string]$ManifestPath = "",
    [ValidateSet("beta", "stable")]
    [string]$Channel = "stable",
    [string]$DownloadRoot = "C:\ProgramData\WebkassaIikoFrontAdapter\updates",
    [string]$IikoFrontPluginsRoot = "C:\Program Files\iiko\iikoRMS\Front.Net\Plugins",
    [string]$ProgramDataRoot = "C:\ProgramData\WebkassaIikoFrontAdapter",
    [string]$InstallRoot = "C:\Program Files\WebkassaIikoFrontAdapter",
    [string]$IikoFrontUser = "",
    [string]$NodePath = "C:\Program Files\nodejs\node.exe",
    [string]$ServiceName = "WebkassaIikoFrontSidecar",
    [string]$HostAddress = "127.0.0.1",
    [int]$Port = 17777,
    [switch]$StopIikoFront,
    [switch]$NoStartSidecar,
    [switch]$DryRun,
    [switch]$Force,
    [switch]$AllowLocalPackage,
    [switch]$AllowDowngrade,
    [string[]]$TrustedDownloadHosts = @("iiko-plugin.kz")
)

$ErrorActionPreference = "Stop"

function Test-IsAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Write-UpdateLog([object]$Entry) {
    $logDir = Join-Path $DownloadRoot "logs"
    New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    $path = Join-Path $logDir ("updater-" + (Get-Date -Format "yyyy-MM-dd") + ".jsonl")
    $line = $Entry | ConvertTo-Json -Depth 8 -Compress
    Add-Content -Encoding UTF8 -LiteralPath $path -Value $line
}

function Read-Manifest {
    if (-not [string]::IsNullOrWhiteSpace($ManifestPath)) {
        if (-not (Test-Path $ManifestPath)) {
            throw "Manifest file was not found: $ManifestPath"
        }
        return Get-Content -Raw -LiteralPath $ManifestPath | ConvertFrom-Json
    }

    if ([string]::IsNullOrWhiteSpace($ManifestUrl)) {
        throw "Pass -ManifestUrl or -ManifestPath."
    }

    $null = Assert-TrustedHttpsUri -Value $ManifestUrl -Label "ManifestUrl"

    return Invoke-RestMethod -Uri $ManifestUrl -TimeoutSec 30
}

function Assert-TrustedHttpsUri([string]$Value, [string]$Label) {
    $uri = $null
    if (-not [Uri]::TryCreate($Value, [UriKind]::Absolute, [ref]$uri) -or $uri.Scheme -ne "https") {
        throw "$Label must be an absolute HTTPS URL."
    }
    if ($TrustedDownloadHosts -notcontains $uri.DnsSafeHost.ToLowerInvariant()) {
        throw "$Label host '$($uri.DnsSafeHost)' is not in TrustedDownloadHosts."
    }
    return $uri
}

function Assert-Manifest([object]$Manifest) {
    if ([int]$Manifest.schemaVersion -ne 1) { throw "Manifest schemaVersion must be 1." }
    if ([string]$Manifest.project -ne "webkassa") { throw "Manifest project must be 'webkassa'." }
    if ([string]$Manifest.channel -ne $Channel) { throw "Manifest channel '$($Manifest.channel)' does not match requested channel '$Channel'." }
    $parsedVersion = Parse-SemVer -Value ([string]$Manifest.version)
    if ($Channel -eq "stable" -and -not [string]::IsNullOrWhiteSpace($parsedVersion.Prerelease)) { throw "Stable channel cannot install a prerelease version." }
    if ($Channel -eq "beta" -and [string]::IsNullOrWhiteSpace($parsedVersion.Prerelease)) { throw "Beta channel version must contain a prerelease suffix." }
    if ([string]$Manifest.minIikoFrontApiVersion -ne "V9") { throw "Manifest minIikoFrontApiVersion must be V9." }
    if (@($Manifest.supportedIikoFrontApiVersions) -notcontains "V9") { throw "Manifest supportedIikoFrontApiVersions must include V9." }
    $publishedAt = [DateTimeOffset]::MinValue
    if ([string]$Manifest.publishedAt -notmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$' -or
        -not [DateTimeOffset]::TryParse([string]$Manifest.publishedAt, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$publishedAt)) {
        throw "Manifest publishedAt must be RFC3339/ISO-8601 with an explicit offset."
    }
    if ([long]$Manifest.packageSize -le 0 -or [long]$Manifest.packageSize -gt 536870912) { throw "Manifest packageSize must be between 1 byte and 512 MiB." }
    if ([string]$Manifest.sha256 -notmatch '^[0-9a-fA-F]{64}$') { throw "Manifest sha256 must contain 64 hexadecimal characters." }
    if (-not $Manifest.packagePath) {
        $packageUri = Assert-TrustedHttpsUri -Value ([string]$Manifest.packageUrl) -Label "packageUrl"
        if ([string]$Manifest.packageFileName -ne [IO.Path]::GetFileName($packageUri.AbsolutePath)) { throw "Manifest packageFileName must match packageUrl." }
    } elseif ([string]$Manifest.packageFileName -ne [IO.Path]::GetFileName([string]$Manifest.packagePath)) {
        throw "Manifest packageFileName must match packagePath."
    }
    if ([IO.Path]::GetExtension([string]$Manifest.packageFileName) -ne ".zip") { throw "Manifest packageFileName must be a ZIP archive." }
}

function Parse-SemVer([string]$Value) {
    $pattern = '^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)(?:-(?<pre>(?:0|[1-9]\d*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*))*))?$'
    $match = [Regex]::Match($Value, $pattern)
    if (-not $match.Success) { throw "Version '$Value' is not valid SemVer." }
    return [pscustomobject]@{
        Core = [Version]::new([int]$match.Groups['major'].Value, [int]$match.Groups['minor'].Value, [int]$match.Groups['patch'].Value)
        Prerelease = $match.Groups['pre'].Value
    }
}

function Compare-SemVer([string]$Left, [string]$Right) {
    $leftVersion = Parse-SemVer -Value $Left
    $rightVersion = Parse-SemVer -Value $Right
    $coreComparison = $leftVersion.Core.CompareTo($rightVersion.Core)
    if ($coreComparison -ne 0) { return $coreComparison }
    if ([string]::IsNullOrWhiteSpace($leftVersion.Prerelease)) {
        return $(if ([string]::IsNullOrWhiteSpace($rightVersion.Prerelease)) { 0 } else { 1 })
    }
    if ([string]::IsNullOrWhiteSpace($rightVersion.Prerelease)) { return -1 }

    $leftIdentifiers = $leftVersion.Prerelease.Split('.')
    $rightIdentifiers = $rightVersion.Prerelease.Split('.')
    for ($index = 0; $index -lt [Math]::Min($leftIdentifiers.Length, $rightIdentifiers.Length); $index++) {
        $leftNumber = [long]0
        $rightNumber = [long]0
        $leftNumeric = [long]::TryParse($leftIdentifiers[$index], [ref]$leftNumber)
        $rightNumeric = [long]::TryParse($rightIdentifiers[$index], [ref]$rightNumber)
        if ($leftNumeric -and $rightNumeric -and $leftNumber -ne $rightNumber) { return $leftNumber.CompareTo($rightNumber) }
        if ($leftNumeric -and -not $rightNumeric) { return -1 }
        if (-not $leftNumeric -and $rightNumeric) { return 1 }
        $identifierComparison = [string]::CompareOrdinal($leftIdentifiers[$index], $rightIdentifiers[$index])
        if ($identifierComparison -ne 0) { return $identifierComparison }
    }
    return $leftIdentifiers.Length.CompareTo($rightIdentifiers.Length)
}

function Resolve-CurrentVersion {
    $versionPath = Join-Path $IikoFrontPluginsRoot "Resto.Front.Api.Webkassa.V9\VERSION"
    if (-not (Test-Path $versionPath)) {
        return ""
    }

    return (Get-Content -Raw -LiteralPath $versionPath).Trim()
}

function Save-Package([object]$Manifest, [string]$TargetDir) {
    New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null

    if ($Manifest.packagePath) {
        if (-not $AllowLocalPackage) {
            throw "Manifest packagePath is allowed only with -AllowLocalPackage."
        }

        $sourcePath = [string]$Manifest.packagePath
        if (-not (Test-Path $sourcePath)) {
            throw "Local packagePath was not found: $sourcePath"
        }

        $targetPath = Join-Path $TargetDir (Split-Path -Leaf $sourcePath)
        Copy-Item -LiteralPath $sourcePath -Destination $targetPath -Force
        return $targetPath
    }

    $packageUrl = [string]$Manifest.packageUrl
    if ([string]::IsNullOrWhiteSpace($packageUrl)) {
        throw "Manifest must contain packageUrl."
    }

    $packageUri = Assert-TrustedHttpsUri -Value $packageUrl -Label "packageUrl"

    $fileName = Split-Path -Leaf $packageUri.AbsolutePath
    if ([string]::IsNullOrWhiteSpace($fileName)) {
        throw "Cannot determine package file name from packageUrl."
    }

    $targetPath = Join-Path $TargetDir $fileName
    Invoke-WebRequest -Uri $packageUrl -OutFile $targetPath -TimeoutSec 120
    return $targetPath
}

function Assert-Sha256([string]$Path, [string]$ExpectedSha256) {
    if ([string]::IsNullOrWhiteSpace($ExpectedSha256)) {
        throw "Manifest sha256 is required."
    }

    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
    $expected = $ExpectedSha256.Trim().ToLowerInvariant()
    if ($actual -ne $expected) {
        throw "Package SHA256 mismatch. Expected=$expected Actual=$actual"
    }

    return $actual
}

function Assert-PackageSize([string]$Path, [long]$ExpectedSize) {
    $actualSize = (Get-Item -LiteralPath $Path).Length
    if ($actualSize -ne $ExpectedSize) {
        throw "Package size mismatch. Expected=$ExpectedSize Actual=$actualSize"
    }
    return $actualSize
}

function Assert-IikoFrontCanUpdate {
    $running = Get-Process Resto.Front.Main, Resto.Front.Api.Host -ErrorAction SilentlyContinue
    if ($running -and -not $StopIikoFront) {
        $names = ($running | ForEach-Object { "$($_.ProcessName):$($_.Id)" }) -join ","
        throw "iikoFront is running ($names). Close it manually or pass -StopIikoFront."
    }
}

function Expand-Package([string]$PackagePath, [string]$StageDir) {
    if (Test-Path $StageDir) {
        Remove-Item -LiteralPath $StageDir -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $StageDir | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $stageRoot = ([IO.Path]::GetFullPath($StageDir)).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        if ($archive.Entries.Count -gt 10000) { throw "Package contains too many ZIP entries." }
        $totalExpandedSize = [long]0
        foreach ($entry in $archive.Entries) {
            if ($entry.FullName -match '^[\\/]' -or $entry.FullName.Contains(':')) { throw "Package contains an unsafe ZIP entry name." }
            $totalExpandedSize += [long]$entry.Length
            if ($totalExpandedSize -gt 536870912) { throw "Expanded package exceeds 512 MiB." }
            $destination = [IO.Path]::GetFullPath((Join-Path $StageDir $entry.FullName))
            if (-not $destination.StartsWith($stageRoot, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Package contains an unsafe ZIP path: $($entry.FullName)"
            }
        }
    } finally {
        $archive.Dispose()
    }
    Expand-Archive -LiteralPath $PackagePath -DestinationPath $StageDir -Force

    $installer = Join-Path $StageDir "install-iikofront-terminal.ps1"
    if (-not (Test-Path $installer)) {
        throw "Package does not contain install-iikofront-terminal.ps1."
    }

    return $installer
}

function Invoke-TerminalInstaller([string]$Installer, [string]$StageDir) {
    $installerParameters = @{
        PackageRoot = $StageDir
        IikoFrontPluginsRoot = $IikoFrontPluginsRoot
        ProgramDataRoot = $ProgramDataRoot
        InstallRoot = $InstallRoot
        NodePath = $NodePath
        ServiceName = $ServiceName
        HostAddress = $HostAddress
        Port = $Port
    }

    if (-not [string]::IsNullOrWhiteSpace($IikoFrontUser)) {
        $installerParameters.IikoFrontUser = $IikoFrontUser
    }

    if ($StopIikoFront) {
        $installerParameters.StopIikoFront = $true
    }

    if (-not $NoStartSidecar) {
        $installerParameters.StartSidecar = $true
    }

    & $Installer @installerParameters
}

function Test-PostInstall([string]$ExpectedVersion) {
    $installed = Resolve-CurrentVersion
    if ($installed -ne $ExpectedVersion) {
        throw "Installed version mismatch. Expected=$ExpectedVersion Actual=$installed"
    }

    if (-not $NoStartSidecar) {
        $status = Invoke-RestMethod -Uri "http://$HostAddress`:$Port/health" -TimeoutSec 10
        if (-not $status.ok) {
            throw "Sidecar status is not ok after update."
        }
    }

    return $installed
}

if (-not (Test-IsAdmin)) {
    throw "Run this updater from an elevated PowerShell session."
}

$startedAt = Get-Date
$manifest = Read-Manifest
Assert-Manifest -Manifest $manifest

$currentVersion = Resolve-CurrentVersion
$targetVersion = [string]$manifest.version
$versionComparison = if ([string]::IsNullOrWhiteSpace($currentVersion)) { 1 } else { Compare-SemVer -Left $targetVersion -Right $currentVersion }
if ($versionComparison -lt 0 -and -not $AllowDowngrade) {
    throw "Downgrade from $currentVersion to $targetVersion is blocked. Pass -AllowDowngrade only for an approved rollback."
}
$updateAvailable = $Force -or ($currentVersion -ne $targetVersion)

$summary = [ordered]@{
    event = "webkassa.updater.plan"
    startedAt = $startedAt.ToString("o")
    channel = $Channel
    currentVersion = $currentVersion
    targetVersion = $targetVersion
    updateAvailable = $updateAvailable
    dryRun = [bool]$DryRun
}

Write-UpdateLog $summary

if (-not $updateAvailable) {
    [pscustomobject]$summary | Format-List
    return
}

Assert-IikoFrontCanUpdate

$releaseDir = Join-Path $DownloadRoot (Join-Path $Channel $targetVersion)
$packagePath = Save-Package -Manifest $manifest -TargetDir $releaseDir
$actualPackageSize = Assert-PackageSize -Path $packagePath -ExpectedSize ([long]$manifest.packageSize)
$actualSha256 = Assert-Sha256 -Path $packagePath -ExpectedSha256 ([string]$manifest.sha256)
$stageDir = Join-Path $releaseDir "stage"

if ($DryRun) {
    $summary.event = "webkassa.updater.dry_run"
    $summary.packagePath = $packagePath
    $summary.sha256 = $actualSha256
    Write-UpdateLog $summary
    [pscustomobject]$summary | Format-List
    return
}

$installer = Expand-Package -PackagePath $packagePath -StageDir $stageDir
Invoke-TerminalInstaller -Installer $installer -StageDir $stageDir
$installedVersion = Test-PostInstall -ExpectedVersion $targetVersion

$result = [ordered]@{
    event = "webkassa.updater.installed"
    startedAt = $startedAt.ToString("o")
    finishedAt = (Get-Date).ToString("o")
    channel = $Channel
    previousVersion = $currentVersion
    installedVersion = $installedVersion
    packagePath = $packagePath
    sha256 = $actualSha256
}

Write-UpdateLog $result
[pscustomobject]$result | Format-List
