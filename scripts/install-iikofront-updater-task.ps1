param(
    [Parameter(Mandatory = $true)]
    [string]$ManifestUrl,
    [ValidateSet("beta", "stable")]
    [string]$Channel = "stable",
    [string]$TaskName = "WebkassaIikoFrontUpdater",
    [string]$UpdaterPath = "C:\Program Files\WebkassaIikoFrontAdapter\updater\update-iikofront-terminal.ps1",
    [int]$IntervalMinutes = 60,
    [switch]$RunAsSystem,
    [switch]$Disabled
)

$ErrorActionPreference = "Stop"

function Test-IsAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsAdmin)) {
    throw "Run this installer from an elevated PowerShell session."
}

if (-not $ManifestUrl.StartsWith("https://", [StringComparison]::OrdinalIgnoreCase)) {
    throw "ManifestUrl must use HTTPS."
}

if (-not (Test-Path $UpdaterPath)) {
    throw "Updater script was not found: $UpdaterPath"
}

if ($IntervalMinutes -lt 15) {
    throw "IntervalMinutes must be at least 15."
}

$actionArgs = @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", ('"{0}"' -f $UpdaterPath),
    "-ManifestUrl", ('"{0}"' -f $ManifestUrl),
    "-Channel", $Channel
) -join " "

$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument $actionArgs
$trigger = New-ScheduledTaskTrigger `
    -Once `
    -At (Get-Date).AddMinutes(5) `
    -RepetitionInterval (New-TimeSpan -Minutes $IntervalMinutes) `
    -RepetitionDuration (New-TimeSpan -Days 3650)
$settings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 30) -StartWhenAvailable

if ($RunAsSystem) {
    $principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -RunLevel Highest
} else {
    $principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -RunLevel Highest
}

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Force | Out-Null

if ($Disabled) {
    Disable-ScheduledTask -TaskName $TaskName | Out-Null
}

[pscustomobject]@{
    TaskName = $TaskName
    Channel = $Channel
    ManifestUrl = $ManifestUrl
    IntervalMinutes = $IntervalMinutes
    Disabled = [bool]$Disabled
    UpdaterPath = $UpdaterPath
} | Format-List
