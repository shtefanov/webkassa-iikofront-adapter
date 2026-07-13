param(
    [string]$PackageRoot = $PSScriptRoot,
    [string]$IikoFrontPluginsRoot = "C:\Program Files\iiko\iikoRMS\Front.Net\Plugins",
    [string]$ProgramDataRoot = "C:\ProgramData\WebkassaIikoFrontAdapter",
    [string]$InstallRoot = "C:\Program Files\WebkassaIikoFrontAdapter",
    [string]$IikoFrontUser = "",
    [string]$NodePath = "C:\Program Files\nodejs\node.exe",
    [string]$ServiceName = "WebkassaIikoFrontSidecar",
    [string]$HostAddress = "127.0.0.1",
    [int]$Port = 17777,
    [switch]$StopIikoFront,
    [switch]$StartSidecar
)

$ErrorActionPreference = "Stop"

function Test-IsAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Resolve-RequiredPath([string]$Path, [string]$Description) {
    if (-not (Test-Path $Path)) {
        throw "$Description was not found: $Path"
    }

    return (Resolve-Path $Path).Path
}

function Grant-Modify([string]$Path, [string]$Account) {
    if ([string]::IsNullOrWhiteSpace($Account)) {
        return
    }

    if (-not (Test-Path $Path)) {
        return
    }

    $item = Get-Item -LiteralPath $Path
    if ($item.PSIsContainer) {
        & icacls $Path /grant "$Account`:(OI)(CI)M" /T | Out-Null
    } else {
        & icacls $Path /grant "$Account`:M" | Out-Null
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to grant Modify permission on $Path to $Account."
    }
}

function Copy-CleanDirectory([string]$Source, [string]$Destination) {
    if (Test-Path $Destination) {
        Remove-Item -LiteralPath $Destination -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Copy-Item -Path (Join-Path $Source "*") -Destination $Destination -Recurse -Force
}

if (-not (Test-IsAdmin)) {
    throw "Run this installer from an elevated PowerShell session."
}

$packageRootPath = Resolve-RequiredPath $PackageRoot "Package root"
$versionPath = Resolve-RequiredPath (Join-Path $packageRootPath "VERSION") "Package VERSION"
$version = (Get-Content -Raw -LiteralPath $versionPath).Trim()
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Package VERSION is empty."
}

$pluginDll = Resolve-RequiredPath (Join-Path $packageRootPath "Webkassa.IikoFrontAdapter.Spike.dll") "Plugin DLL"
$setupExe = Resolve-RequiredPath (Join-Path $packageRootPath "setup\Webkassa.IikoFrontAdapter.Setup.exe") "Setup utility"
$sidecarServiceSource = Resolve-RequiredPath (Join-Path $packageRootPath "sidecar-service") "Sidecar service package"
$sidecarRuntimeSource = Resolve-RequiredPath (Join-Path $packageRootPath "sidecar-runtime") "Sidecar runtime package"
Resolve-RequiredPath (Join-Path $sidecarRuntimeSource "scripts\sidecar.js") "Sidecar script" | Out-Null
Resolve-RequiredPath (Join-Path $sidecarRuntimeSource "src\sidecar-server.js") "Sidecar runtime source" | Out-Null

if (-not (Test-Path $NodePath)) {
    throw "Node.js was not found at $NodePath. Install Node.js on the terminal or pass -NodePath."
}

if (-not (Test-Path $IikoFrontPluginsRoot)) {
    throw "iikoFront Plugins folder was not found: $IikoFrontPluginsRoot. Pass -IikoFrontPluginsRoot for this terminal."
}

$targetUser = $IikoFrontUser
if ([string]::IsNullOrWhiteSpace($targetUser)) {
    $targetUser = "$env:USERDOMAIN\$env:USERNAME"
}

$pluginPath = Join-Path $IikoFrontPluginsRoot "Webkassa.IikoFrontAdapter.Spike"
$backupRoot = Join-Path $ProgramDataRoot "backups"
$sidecarServiceInstallRoot = Join-Path $InstallRoot "sidecar-service"
$sidecarRuntimeInstallRoot = Join-Path $InstallRoot "sidecar-runtime"
$configDir = Join-Path $ProgramDataRoot "config"
$configPath = Join-Path $configDir "webkassa-adapter.config.json"
$logsDir = Join-Path $ProgramDataRoot "logs"
$sidecarDataDir = Join-Path $ProgramDataRoot "sidecar"
$managedDirs = @(
    $configDir,
    (Join-Path $ProgramDataRoot "secrets"),
    (Join-Path $ProgramDataRoot "exports"),
    (Join-Path $ProgramDataRoot "nkt-cache"),
    (Join-Path $ProgramDataRoot "nkt-drafts"),
    (Join-Path $ProgramDataRoot "nkt-batches"),
    (Join-Path $ProgramDataRoot "nkt-queue"),
    (Join-Path $ProgramDataRoot "nkt-store"),
    (Join-Path $ProgramDataRoot "webnkt-imports"),
    (Join-Path $ProgramDataRoot "state"),
    $logsDir,
    $sidecarDataDir,
    $backupRoot
)

foreach ($dir in $managedDirs) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
}

foreach ($dir in $managedDirs) {
    Grant-Modify -Path $dir -Account $targetUser
}

if (-not (Test-Path $configPath)) {
    $configSource = Join-Path $packageRootPath "config\iikofront-adapter.config.example.json"
    Resolve-RequiredPath $configSource "Config example" | Out-Null
    Copy-Item -LiteralPath $configSource -Destination $configPath -Force
    Grant-Modify -Path $configPath -Account $targetUser
}

$runningFront = Get-Process Resto.Front.Main, Resto.Front.Api.Host -ErrorAction SilentlyContinue
if ($runningFront -and -not $StopIikoFront) {
    throw "iikoFront is running. Close it manually or rerun with -StopIikoFront."
}

if ($runningFront -and $StopIikoFront) {
    $runningFront | Stop-Process -Force
    Start-Sleep -Seconds 3
}

$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService -and $existingService.Status -ne "Stopped") {
    Stop-Service -Name $ServiceName -Force
    Start-Sleep -Seconds 2
}

if (Test-Path $pluginPath) {
    New-Item -ItemType Directory -Force -Path $backupRoot | Out-Null
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $backupPath = Join-Path $backupRoot "Webkassa.IikoFrontAdapter.Spike-$stamp"
    Move-Item -LiteralPath $pluginPath -Destination $backupPath
}

New-Item -ItemType Directory -Force -Path $pluginPath | Out-Null
Get-ChildItem -Path $packageRootPath -File |
    Where-Object { $_.Name -notlike "install-*.ps1" } |
    Copy-Item -Destination $pluginPath -Force

Copy-CleanDirectory -Source $sidecarServiceSource -Destination $sidecarServiceInstallRoot
Copy-CleanDirectory -Source $sidecarRuntimeSource -Destination $sidecarRuntimeInstallRoot

$serviceExe = Join-Path $sidecarServiceInstallRoot "Webkassa.Sidecar.WindowsService.exe"
Resolve-RequiredPath $serviceExe "Sidecar service executable" | Out-Null

$imagePath = '"' + $serviceExe + '"' +
    ' --project-root "' + $sidecarRuntimeInstallRoot + '"' +
    ' --config "' + $configPath + '"' +
    ' --node "' + $NodePath + '"' +
    ' --host "' + $HostAddress + '"' +
    ' --port ' + $Port +
    ' --data-dir "' + $sidecarDataDir + '"' +
    ' --log-dir "' + $logsDir + '"'

if ($existingService) {
    Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName" -Name ImagePath -Value $imagePath
    Set-Service -Name $ServiceName -StartupType Automatic
} else {
    New-Service -Name $ServiceName -BinaryPathName $imagePath -DisplayName "Webkassa iikoFront Sidecar" -StartupType Automatic | Out-Null
}

& sc.exe description $ServiceName "Runs the local Webkassa sidecar for iikoFront on this terminal." | Out-Null

if ($StartSidecar) {
    Start-Service -Name $ServiceName
    Start-Sleep -Seconds 3
}

$service = Get-Service -Name $ServiceName
$installedVersion = (Get-Content -Raw -LiteralPath (Join-Path $pluginPath "VERSION")).Trim()

[pscustomobject]@{
    PackageVersion = $version
    InstalledPluginVersion = $installedVersion
    PluginPath = $pluginPath
    ConfigPath = $configPath
    ProgramDataRoot = $ProgramDataRoot
    IikoFrontUser = $targetUser
    SidecarService = $ServiceName
    SidecarServiceStatus = $service.Status.ToString()
    SidecarRuntimeRoot = $sidecarRuntimeInstallRoot
    SetupUtility = $setupExe
} | Format-List

Write-Host "Next: open iikoFront, go to Settings Webkassa, enter Webkassa/National Catalog secrets, save, then start or restart $ServiceName."
