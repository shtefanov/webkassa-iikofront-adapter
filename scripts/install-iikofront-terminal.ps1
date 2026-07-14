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

function Grant-Read([string]$Path, [string]$Account) {
    if ([string]::IsNullOrWhiteSpace($Account) -or -not (Test-Path $Path)) { return }
    & icacls $Path /grant "$Account`:(OI)(CI)RX" /T | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to grant read permission on $Path to $Account." }
}

function Protect-ServiceDirectory([string]$Path, [string]$UntrustedAccount) {
    if (-not (Test-Path $Path)) { return }

    $systemSid = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
    $administratorsSid = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')

    # Older hardened installs can contain protected child ACLs owned by SYSTEM.
    # Normalize ownership first, then make every child inherit a deterministic
    # SYSTEM/Administrators-only ACL from the service directory root.
    & icacls $Path /setowner '*S-1-5-32-544' /T /C | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to normalize ownership for $Path." }

    $acl = New-Object Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true, $false)
    $acl.SetOwner($administratorsSid)
    $inheritance = [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit
    $propagation = [Security.AccessControl.PropagationFlags]::None
    $allow = [Security.AccessControl.AccessControlType]::Allow
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($systemSid, [Security.AccessControl.FileSystemRights]::FullControl, $inheritance, $propagation, $allow))
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($administratorsSid, [Security.AccessControl.FileSystemRights]::FullControl, $inheritance, $propagation, $allow))
    Set-Acl -LiteralPath $Path -AclObject $acl

    Get-ChildItem -LiteralPath $Path -Force | ForEach-Object {
        & icacls $_.FullName /reset /T /C | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Failed to reset child ACLs under $Path." }
    }

    $allowedSids = @($systemSid.Value, $administratorsSid.Value)
    @((Get-Item -LiteralPath $Path)) + @(Get-ChildItem -LiteralPath $Path -Recurse -Force) | ForEach-Object {
        $itemAcl = Get-Acl -LiteralPath $_.FullName
        foreach ($rule in $itemAcl.Access) {
            if ($rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow) { continue }
            $ruleSid = $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
            if ($allowedSids -notcontains $ruleSid) {
                throw "Unexpected principal $ruleSid retains access to service-only path $($_.FullName)."
            }
        }
    }
}

function Protect-PluginWritableDirectory([string]$Path, [string]$Account) {
    if (-not (Test-Path $Path)) { return }
    if ([string]::IsNullOrWhiteSpace($Account)) { throw "Target iikoFront account is required for $Path." }

    $systemSid = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
    $administratorsSid = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
    $targetSid = ([Security.Principal.NTAccount]::new($Account)).Translate([Security.Principal.SecurityIdentifier])

    & icacls $Path /setowner '*S-1-5-32-544' /T /C | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to normalize ownership for $Path." }

    $acl = New-Object Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true, $false)
    $acl.SetOwner($administratorsSid)
    $inheritance = [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit
    $propagation = [Security.AccessControl.PropagationFlags]::None
    $allow = [Security.AccessControl.AccessControlType]::Allow
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($systemSid, [Security.AccessControl.FileSystemRights]::FullControl, $inheritance, $propagation, $allow))
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($administratorsSid, [Security.AccessControl.FileSystemRights]::FullControl, $inheritance, $propagation, $allow))
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($targetSid, [Security.AccessControl.FileSystemRights]::Modify, $inheritance, $propagation, $allow))
    Set-Acl -LiteralPath $Path -AclObject $acl

    Get-ChildItem -LiteralPath $Path -Force | ForEach-Object {
        & icacls $_.FullName /reset /T /C | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Failed to reset child ACLs under $Path." }
    }

    $allowedSids = @($systemSid.Value, $administratorsSid.Value, $targetSid.Value)
    @((Get-Item -LiteralPath $Path)) + @(Get-ChildItem -LiteralPath $Path -Recurse -Force) | ForEach-Object {
        $itemAcl = Get-Acl -LiteralPath $_.FullName
        foreach ($rule in $itemAcl.Access) {
            if ($rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow) { continue }
            $ruleSid = $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
            if ($allowedSids -notcontains $ruleSid) {
                throw "Unexpected principal $ruleSid retains access to plugin-writable path $($_.FullName)."
            }
        }
    }
}

function Copy-CleanDirectory([string]$Source, [string]$Destination) {
    if (Test-Path $Destination) {
        Remove-Item -LiteralPath $Destination -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Copy-Item -Path (Join-Path $Source "*") -Destination $Destination -Recurse -Force
}

function Resolve-DefaultTargetUser {
    if (-not [string]::IsNullOrWhiteSpace($env:USERDOMAIN) -and $env:USERDOMAIN -ne "WORKGROUP") {
        return "$env:USERDOMAIN\$env:USERNAME"
    }

    if (-not [string]::IsNullOrWhiteSpace($env:COMPUTERNAME)) {
        return "$env:COMPUTERNAME\$env:USERNAME"
    }

    return $env:USERNAME
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

$pluginDll = Resolve-RequiredPath (Join-Path $packageRootPath "Resto.Front.Api.Webkassa.V9.dll") "Plugin DLL"
$setupExe = Resolve-RequiredPath (Join-Path $packageRootPath "setup\Webkassa.IikoFrontAdapter.Setup.exe") "Setup utility"
$sidecarServiceSource = Resolve-RequiredPath (Join-Path $packageRootPath "sidecar-service") "Sidecar service package"
$sidecarRuntimeSource = Resolve-RequiredPath (Join-Path $packageRootPath "sidecar-runtime") "Sidecar runtime package"
$updaterSource = Resolve-RequiredPath (Join-Path $packageRootPath "updater") "Updater package"
Resolve-RequiredPath (Join-Path $sidecarRuntimeSource "scripts\sidecar.js") "Sidecar script" | Out-Null
Resolve-RequiredPath (Join-Path $sidecarRuntimeSource "src\sidecar-server.js") "Sidecar runtime source" | Out-Null
Resolve-RequiredPath (Join-Path $updaterSource "update-iikofront-terminal.ps1") "Updater script" | Out-Null

if (-not (Test-Path $NodePath)) {
    throw "Node.js was not found at $NodePath. Install Node.js on the terminal or pass -NodePath."
}

if (-not (Test-Path $IikoFrontPluginsRoot)) {
    throw "iikoFront Plugins folder was not found: $IikoFrontPluginsRoot. Pass -IikoFrontPluginsRoot for this terminal."
}

$targetUser = $IikoFrontUser
if ([string]::IsNullOrWhiteSpace($targetUser)) {
    $targetUser = Resolve-DefaultTargetUser
}

$pluginPath = Join-Path $IikoFrontPluginsRoot "Resto.Front.Api.Webkassa.V9"
$legacyPluginPaths = @(
    (Join-Path $IikoFrontPluginsRoot "Webkassa.IikoFrontAdapter.Spike")
)
$backupRoot = Join-Path $ProgramDataRoot "backups"
$sidecarServiceInstallRoot = Join-Path $InstallRoot "sidecar-service"
$sidecarRuntimeInstallRoot = Join-Path $InstallRoot "sidecar-runtime"
$updaterInstallRoot = Join-Path $InstallRoot "updater"
$configDir = Join-Path $ProgramDataRoot "config"
$configPath = Join-Path $configDir "webkassa-adapter.config.json"
$logsDir = Join-Path $ProgramDataRoot "logs"
$sidecarDataDir = Join-Path $ProgramDataRoot "sidecar"
$secretsDir = Join-Path $ProgramDataRoot "secrets"
$sidecarIpcSecretsDir = Join-Path $secretsDir "ipc"
$pluginWritableDirs = @(
    (Join-Path $ProgramDataRoot "exports"),
    (Join-Path $ProgramDataRoot "nkt-cache"),
    (Join-Path $ProgramDataRoot "nkt-drafts"),
    (Join-Path $ProgramDataRoot "nkt-batches"),
    (Join-Path $ProgramDataRoot "nkt-queue"),
    (Join-Path $ProgramDataRoot "nkt-store"),
    (Join-Path $ProgramDataRoot "webnkt-imports"),
    (Join-Path $ProgramDataRoot "state")
)
$serviceOnlyDirs = @($configDir, $secretsDir, $sidecarDataDir, $backupRoot, $logsDir)
$managedDirs = @($pluginWritableDirs) + @($serviceOnlyDirs)

foreach ($dir in $managedDirs) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
}

foreach ($dir in $serviceOnlyDirs) {
    Protect-ServiceDirectory -Path $dir -UntrustedAccount $targetUser
}

foreach ($dir in $pluginWritableDirs) {
    Protect-PluginWritableDirectory -Path $dir -Account $targetUser
}
New-Item -ItemType Directory -Force -Path $sidecarIpcSecretsDir | Out-Null
Grant-Read -Path $sidecarIpcSecretsDir -Account $targetUser
Grant-Read -Path $configDir -Account $targetUser
Grant-Read -Path $logsDir -Account $targetUser

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
    $backupPath = Join-Path $backupRoot "Resto.Front.Api.Webkassa.V9-$stamp"
    Move-Item -LiteralPath $pluginPath -Destination $backupPath
}

foreach ($legacyPluginPath in $legacyPluginPaths) {
    if (Test-Path $legacyPluginPath) {
        New-Item -ItemType Directory -Force -Path $backupRoot | Out-Null
        $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
        $legacyName = Split-Path -Leaf $legacyPluginPath
        $backupPath = Join-Path $backupRoot "$legacyName-$stamp"
        Move-Item -LiteralPath $legacyPluginPath -Destination $backupPath
    }
}

New-Item -ItemType Directory -Force -Path $pluginPath | Out-Null
Get-ChildItem -Path $packageRootPath -File |
    Where-Object { $_.Name -notlike "install-*.ps1" } |
    Copy-Item -Destination $pluginPath -Force

Copy-CleanDirectory -Source $sidecarServiceSource -Destination $sidecarServiceInstallRoot
Copy-CleanDirectory -Source $sidecarRuntimeSource -Destination $sidecarRuntimeInstallRoot
Copy-CleanDirectory -Source $updaterSource -Destination $updaterInstallRoot

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
if ($LASTEXITCODE -ne 0) { throw "Failed to set the $ServiceName description." }
& sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Failed to configure $ServiceName recovery actions." }
& sc.exe failureflag $ServiceName 1 | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Failed to enable $ServiceName recovery for non-crash failures." }

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
    UpdaterRoot = $updaterInstallRoot
    SetupUtility = $setupExe
} | Format-List

Write-Host "Next: open iikoFront, go to Settings Webkassa, enter Webkassa/National Catalog secrets, save, then start or restart $ServiceName."
