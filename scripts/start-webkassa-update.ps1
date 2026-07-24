[CmdletBinding()]
param(
    [ValidateSet("beta", "stable")]
    [string]$Channel = "beta",
    [switch]$WaitForKey
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

if (-not (Test-IsAdministrator)) {
    $arguments = @(
        "-NoProfile",
        "-ExecutionPolicy Bypass",
        ('-File "{0}"' -f $PSCommandPath),
        ("-Channel {0}" -f $Channel)
    )
    if ($WaitForKey) {
        $arguments += "-WaitForKey"
    }

    try {
        $process = Start-Process -FilePath "powershell.exe" -Verb RunAs -Wait -PassThru -ArgumentList $arguments
        exit $process.ExitCode
    } catch {
        Write-Host "Webkassa update was cancelled or could not obtain administrator rights." -ForegroundColor Yellow
        exit 1
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
