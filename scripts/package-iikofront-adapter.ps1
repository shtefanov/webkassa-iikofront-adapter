param(
    [string]$Configuration = "Release",
    [string]$ProjectPath = "src/Resto.Front.Api.Webkassa.V9/Resto.Front.Api.Webkassa.V9.csproj",
    [string]$SetupProjectPath = "tools/Webkassa.IikoFrontAdapter.Setup/Webkassa.IikoFrontAdapter.Setup.csproj",
    [string]$SidecarServiceProjectPath = "tools/Webkassa.Sidecar.WindowsService/Webkassa.Sidecar.WindowsService.csproj",
    [string]$OutputRoot = "dist/iikofront-adapter"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectFile = Resolve-Path (Join-Path $repoRoot $ProjectPath)
$projectDir = Split-Path -Parent $projectFile
$setupProjectFile = Resolve-Path (Join-Path $repoRoot $SetupProjectPath)
$setupProjectDir = Split-Path -Parent $setupProjectFile
$sidecarServiceProjectFile = Resolve-Path (Join-Path $repoRoot $SidecarServiceProjectPath)
$sidecarServiceProjectDir = Split-Path -Parent $sidecarServiceProjectFile
$packageName = "Resto.Front.Api.Webkassa.V9"
$versionFile = Join-Path $repoRoot "VERSION"
if (-not (Test-Path $versionFile)) {
    throw "VERSION file was not found: $versionFile"
}
$version = (Get-Content $versionFile -Raw).Trim()
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "VERSION file is empty: $versionFile"
}
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$outputRootPath = Join-Path $repoRoot $OutputRoot
$stageDir = Join-Path $outputRootPath "stage/$packageName"
$zipPath = Join-Path $outputRootPath "$packageName-$version-$timestamp.zip"

Write-Host "Packaging $packageName"
Write-Host "Version: $version"
Write-Host "Project: $projectFile"
Write-Host "Setup project: $setupProjectFile"
Write-Host "Sidecar service project: $sidecarServiceProjectFile"
Write-Host "Configuration: $Configuration"

dotnet restore $projectFile
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE"
}

dotnet build $projectFile --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}

dotnet restore $setupProjectFile
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore setup failed with exit code $LASTEXITCODE"
}

dotnet build $setupProjectFile --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build setup failed with exit code $LASTEXITCODE"
}

dotnet restore $sidecarServiceProjectFile
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore sidecar service failed with exit code $LASTEXITCODE"
}

dotnet build $sidecarServiceProjectFile --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build sidecar service failed with exit code $LASTEXITCODE"
}

$buildDir = Join-Path $projectDir "bin/$Configuration"
$setupBuildDir = Join-Path $setupProjectDir "bin/$Configuration/net472"
$sidecarServiceBuildDir = Join-Path $sidecarServiceProjectDir "bin/$Configuration/net472"
$assemblyPath = Join-Path $buildDir "$packageName.dll"
if (-not (Test-Path $assemblyPath)) {
    throw "Build output was not found: $assemblyPath"
}
$setupExePath = Join-Path $setupBuildDir "Webkassa.IikoFrontAdapter.Setup.exe"
if (-not (Test-Path $setupExePath)) {
    throw "Setup output was not found: $setupExePath"
}
$sidecarServiceExePath = Join-Path $sidecarServiceBuildDir "Webkassa.Sidecar.WindowsService.exe"
if (-not (Test-Path $sidecarServiceExePath)) {
    throw "Sidecar service output was not found: $sidecarServiceExePath"
}

if (Test-Path $stageDir) {
    Remove-Item $stageDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $stageDir | Out-Null

Get-ChildItem -Path $buildDir -File |
    Where-Object { $_.Extension -in ".dll", ".config", ".json", ".xml" } |
    Copy-Item -Destination $stageDir

$iikoManifestTemplate = Join-Path $projectDir "Manifest.xml.template"
if (Test-Path $iikoManifestTemplate) {
    Copy-Item $iikoManifestTemplate -Destination $stageDir
}

$iikoManifest = Join-Path $projectDir "Manifest.xml"
if (Test-Path $iikoManifest) {
    Copy-Item $iikoManifest -Destination $stageDir
}

$setupStageDir = Join-Path $stageDir "setup"
New-Item -ItemType Directory -Force -Path $setupStageDir | Out-Null
Get-ChildItem -Path $setupBuildDir -File |
    Where-Object { $_.Extension -in ".exe", ".dll", ".config", ".json", ".xml" } |
    Copy-Item -Destination $setupStageDir

$sidecarServiceStageDir = Join-Path $stageDir "sidecar-service"
New-Item -ItemType Directory -Force -Path $sidecarServiceStageDir | Out-Null
Get-ChildItem -Path $sidecarServiceBuildDir -File |
    Where-Object { $_.Extension -in ".exe", ".dll", ".config", ".json", ".xml" } |
    Copy-Item -Destination $sidecarServiceStageDir

$sidecarRuntimeStageDir = Join-Path $stageDir "sidecar-runtime"
New-Item -ItemType Directory -Force -Path (Join-Path $sidecarRuntimeStageDir "scripts") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $sidecarRuntimeStageDir "src") | Out-Null
Copy-Item -Path (Join-Path $repoRoot "scripts/sidecar.js") -Destination (Join-Path $sidecarRuntimeStageDir "scripts")
Copy-Item -Path (Join-Path $repoRoot "src/*.js") -Destination (Join-Path $sidecarRuntimeStageDir "src")
Copy-Item -Path (Join-Path $repoRoot "package.json") -Destination $sidecarRuntimeStageDir

$configStageDir = Join-Path $stageDir "config"
New-Item -ItemType Directory -Force -Path $configStageDir | Out-Null
Get-ChildItem -Path (Join-Path $repoRoot "config") -Filter "*.json" -File |
    Where-Object { $_.Name -notlike "update-manifest.*.example.json" } |
    Copy-Item -Destination $configStageDir

Copy-Item -Path (Join-Path $repoRoot "scripts/install-iikofront-terminal.ps1") -Destination $stageDir

$updaterStageDir = Join-Path $stageDir "updater"
New-Item -ItemType Directory -Force -Path $updaterStageDir | Out-Null
Copy-Item -Path (Join-Path $repoRoot "scripts/update-iikofront-terminal.ps1") -Destination $updaterStageDir
Copy-Item -Path (Join-Path $repoRoot "scripts/install-iikofront-updater-task.ps1") -Destination $updaterStageDir
Copy-Item -Path (Join-Path $repoRoot "scripts/new-release-manifest.ps1") -Destination $updaterStageDir

$manifest = [ordered]@{
    package = $packageName
    version = $version
    builtAt = (Get-Date).ToString("o")
    target = "iikoFront external fiscal register"
    iikoFrontApiVersion = "V9"
    iikoFrontMinVersion = "9.5"
    iikoPluginEntryPoint = "Resto.Front.Api.Webkassa.V9.Plugin"
    iikoCashRegisterFactory = "Resto.Front.Api.Webkassa.V9.WebkassaCashRegisterFactory"
    targetFramework = "net472"
    apiPackage = "Resto.Front.Api.V9 installed Front.Net DLL 9.5.7018"
    iikoSdk9ComplianceDoc = "docs/iikofront-sdk9-compliance.md"
    iikoLicenseModuleId = 21016318
    iikoLicenseModuleIdStatus = "interim-assigned"
    iikoLicenseModuleIdRequired = $true
    iikoManifestIncluded = (Test-Path $iikoManifest)
    iikoManifestTemplateIncluded = (Test-Path $iikoManifestTemplate)
    webkassaProtocolVersion = "2.0.3"
    writeFiscalDataRequired = $true
    webkassaAutonomousModeImplemented = $false
    localDeferredQueueDefaultEnabled = $false
    localDeferredQueueMaxHours = 72
    webNktSupported = $true
    webNktFieldMapConfigurable = $true
    sidecarBridgeSupported = $true
    sidecarDefaultBaseUrl = "http://127.0.0.1:17777"
    sidecarBearerAuthenticationRequired = $true
    sidecarStatusEndpoint = "/status"
    sidecarSaleEndpoint = "/fiscalize/sale"
    sidecarReturnEndpoint = "/fiscalize/return"
    sidecarXReportEndpoint = "/reports/x"
    sidecarZReportEndpoint = "/reports/z"
    sidecarMoneyOperationEndpoint = "/money-operation"
    sidecarTicketPrintFormatEndpoint = "/tickets/print-format"
    sidecarOfflineStatusEndpoint = "/offline/status"
    sidecarOfflineSyncEndpoint = "/offline/sync"
    receiptPrintFormatProvider = "Webkassa /api/v4/Ticket/PrintFormat"
    receiptPrintFormatDefaultPaperKind = 0
    redactedFileLogger = $true
    supportBundleWebNktDiagnostics = $true
    fiscalStorageProvider = "durable-json"
    includesSetupUtility = $true
    includesTerminalInstaller = $true
    includesUpdater = $true
    includesSidecarService = $true
    includesSidecarRuntime = $true
    terminalInstaller = "install-iikofront-terminal.ps1"
    updaterPackage = "updater"
    updaterScript = "updater/update-iikofront-terminal.ps1"
    sidecarServicePackage = "sidecar-service"
    sidecarRuntimePackage = "sidecar-runtime"
    deployStatus = "beta; deploy only to approved test or pilot iikoFront terminals"
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 (Join-Path $stageDir "package-manifest.json")
Set-Content -Encoding UTF8 -Path (Join-Path $stageDir "VERSION") -Value $version

@"
Resto.Front.Api.Webkassa.V9

This package is a beta release.
Deploy it only to approved test or pilot iikoFront terminals.

Before deployment:
- confirm iiko demo/developer license covers LicenseModuleId 21016318;
- confirm Manifest.xml LicenseModuleId and [PluginLicenseModuleId(...)] match;
- confirm the target iikoFront plugin folder for this exact installation;
- stop using production cash registers for this test;
- keep Webkassa secrets in protected storage, not in this package;
- capture iikoFront logs before and after plugin loading.

Expected first validation:
- run install-iikofront-terminal.ps1 from an elevated PowerShell session on the target iikoFront terminal;
- run setup\Webkassa.IikoFrontAdapter.Setup.exe --paths;
- for manifest-driven updates, run updater\update-iikofront-terminal.ps1 from an elevated PowerShell session;
- iikoFront loads the plugin without assembly binding errors;
- Webkassa cash register factory appears as an available fiscal register driver;
- no sale/return fiscalization is attempted until configuration is confirmed.
"@ | Set-Content -Encoding UTF8 (Join-Path $stageDir "README-INSTALL.txt")

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}
Compress-Archive -Path (Join-Path $stageDir "*") -DestinationPath $zipPath

Write-Host "Package created: $zipPath"
