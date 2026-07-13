param(
    [Parameter(Mandatory = $true)]
    [string] $PackagePath,

    [string] $PluginPath = "C:\Program Files\iiko\iikoRMS\Front.Net\Plugins\Resto.Front.Api.Webkassa.V9",

    [string] $StagePath = "C:\OpenClaw\work\webkassa\dist\iikofront-adapter\deploy-stage"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $PackagePath)) {
    throw "Package not found: $PackagePath"
}

Get-Process Resto.Front.Main, Resto.Front.Api.Host -ErrorAction SilentlyContinue |
    Stop-Process -Force
Start-Sleep -Seconds 3

if (Test-Path $StagePath) {
    Remove-Item $StagePath -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $StagePath | Out-Null
Expand-Archive -Path $PackagePath -DestinationPath $StagePath -Force

if (-not (Test-Path $PluginPath)) {
    New-Item -ItemType Directory -Force -Path $PluginPath | Out-Null
}

Get-ChildItem -Path $PluginPath -Force |
    Remove-Item -Recurse -Force

Copy-Item -Path (Join-Path $StagePath "*") -Destination $PluginPath -Recurse -Force

$versionPath = Join-Path $PluginPath "VERSION"
if (Test-Path $versionPath) {
    Get-Content $versionPath
}

Get-ChildItem -Path $PluginPath |
    Select-Object Name, Length |
    Format-Table -AutoSize
