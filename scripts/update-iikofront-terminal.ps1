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
    [switch]$AllowLocalPackage
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

    if (-not $ManifestUrl.StartsWith("https://", [StringComparison]::OrdinalIgnoreCase)) {
        throw "ManifestUrl must use HTTPS."
    }

    return Invoke-RestMethod -Uri $ManifestUrl -TimeoutSec 30
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

    if (-not $packageUrl.StartsWith("https://", [StringComparison]::OrdinalIgnoreCase)) {
        throw "packageUrl must use HTTPS."
    }

    $fileName = Split-Path -Leaf ([Uri]$packageUrl).AbsolutePath
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
        $status = Invoke-RestMethod -Uri "http://$HostAddress`:$Port/status" -TimeoutSec 10
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

if ([string]$manifest.channel -ne $Channel) {
    throw "Manifest channel '$($manifest.channel)' does not match requested channel '$Channel'."
}

if ([string]::IsNullOrWhiteSpace([string]$manifest.version)) {
    throw "Manifest version is required."
}

$currentVersion = Resolve-CurrentVersion
$targetVersion = [string]$manifest.version
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
