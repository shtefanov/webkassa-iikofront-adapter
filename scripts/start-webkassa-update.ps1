[CmdletBinding()]
param(
    [ValidateSet("beta", "stable")]
    [string]$Channel = "beta",
    [switch]$WaitForKey,
    [switch]$InternalStaged
)

$ErrorActionPreference = "Stop"

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Wait-ForOperator {
    if ($WaitForKey) {
        $null = Read-Host "Press Enter to close"
    }
}

function Get-LauncherArguments([string]$LauncherPath, [switch]$Staged) {
    $arguments = @(
        "-NoProfile",
        "-ExecutionPolicy Bypass",
        ('-File "{0}"' -f $LauncherPath),
        ("-Channel {0}" -f $Channel)
    )
    if ($WaitForKey) {
        $arguments += "-WaitForKey"
    }
    if ($Staged) {
        $arguments += "-InternalStaged"
    }
    return $arguments
}

if (-not $InternalStaged -and -not (Test-IsAdministrator)) {
    $arguments = Get-LauncherArguments -LauncherPath $PSCommandPath

    try {
        Set-Location ([IO.Path]::GetTempPath())
        $process = Start-Process -FilePath "powershell.exe" -Verb RunAs -Wait -PassThru -ArgumentList $arguments
        exit $process.ExitCode
    } catch {
        Write-Host "Webkassa update was cancelled or could not obtain administrator rights." -ForegroundColor Yellow
        exit 1
    }
}

if (-not (Test-IsAdministrator)) {
    Write-Host "Webkassa updater requires administrator rights." -ForegroundColor Red
    Wait-ForOperator
    exit 1
}

if (-not $InternalStaged) {
    $updaterSource = Join-Path $PSScriptRoot "update-iikofront-terminal.ps1"
    if (-not (Test-Path -LiteralPath $updaterSource -PathType Leaf)) {
        Write-Host "Webkassa updater was not found: $updaterSource" -ForegroundColor Red
        Wait-ForOperator
        exit 1
    }

    $stagingBase = Join-Path ([IO.Path]::GetTempPath()) "WebkassaIikoFrontAdapter\updater-runs"
    $runRoot = Join-Path $stagingBase ([Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
    $stagedLauncher = Join-Path $runRoot "start-webkassa-update.ps1"

    try {
        Copy-Item -LiteralPath $PSCommandPath -Destination $stagedLauncher -Force
        Copy-Item -LiteralPath $updaterSource -Destination $runRoot -Force
        Set-Location $stagingBase
        $arguments = Get-LauncherArguments -LauncherPath $stagedLauncher -Staged
        $process = Start-Process -FilePath "powershell.exe" -Wait -PassThru -ArgumentList $arguments
        exit $process.ExitCode
    } catch {
        Write-Host ("Webkassa updater staging failed: " + $_.Exception.Message) -ForegroundColor Red
        Wait-ForOperator
        exit 1
    } finally {
        Set-Location ([IO.Path]::GetTempPath())
        if (Test-Path -LiteralPath $runRoot) {
            Remove-Item -LiteralPath $runRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

$updaterPath = Join-Path $PSScriptRoot "update-iikofront-terminal.ps1"
if (-not (Test-Path -LiteralPath $updaterPath -PathType Leaf)) {
    Write-Host "Webkassa updater was not found: $updaterPath" -ForegroundColor Red
    Wait-ForOperator
    exit 1
}

$manifestUrl = "https://iiko-plugin.kz/updates/webkassa/$Channel.json"

try {
    Write-Host "Webkassa update channel: $Channel"
    Write-Host "iikoFront will be closed only after the manifest and package pass validation."
    & $updaterPath -ManifestUrl $manifestUrl -Channel $Channel -StopIikoFront
    Write-Host "Webkassa update completed. Start iikoFront again." -ForegroundColor Green
    Wait-ForOperator
    exit 0
} catch {
    Write-Host ("Webkassa update failed: " + $_.Exception.Message) -ForegroundColor Red
    Write-Host "The previous plugin backup remains available under the configured backup directory."
    Wait-ForOperator
    exit 1
}
