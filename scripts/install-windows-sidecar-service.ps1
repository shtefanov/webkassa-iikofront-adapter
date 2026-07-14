param(
    [string]$ProjectRoot = "C:\OpenClaw\work\webkassa",
    [string]$ConfigPath = "C:\ProgramData\WebkassaIikoFrontAdapter\config\webkassa-adapter.config.json",
    [string]$NodePath = "C:\Program Files\nodejs\node.exe",
    [string]$ServiceName = "WebkassaIikoFrontSidecar",
    [string]$DisplayName = "Webkassa iikoFront Sidecar",
    [string]$HostAddress = "127.0.0.1",
    [int]$Port = 17777
)

$ErrorActionPreference = "Stop"

$sourceDir = Join-Path $ProjectRoot "tools\Webkassa.Sidecar.WindowsService\bin\Release\net472"
$source = Join-Path $sourceDir "Webkassa.Sidecar.WindowsService.exe"
if (!(Test-Path $source)) {
    throw "Service executable not found. Build the service project first: $source"
}

$installRoot = "C:\Program Files\WebkassaIikoFrontAdapter\sidecar-service"
New-Item -ItemType Directory -Force -Path $installRoot | Out-Null

$exePath = Join-Path $installRoot "Webkassa.Sidecar.WindowsService.exe"
Copy-Item -Force -Path (Join-Path $sourceDir "*") -Destination $installRoot

$dataDir = "C:\ProgramData\WebkassaIikoFrontAdapter\sidecar"
$logDir = "C:\ProgramData\WebkassaIikoFrontAdapter\logs"
New-Item -ItemType Directory -Force -Path $dataDir, $logDir | Out-Null

$imagePath = '"' + $exePath + '"' +
    ' --project-root "' + $ProjectRoot + '"' +
    ' --config "' + $ConfigPath + '"' +
    ' --node "' + $NodePath + '"' +
    ' --host "' + $HostAddress + '"' +
    ' --port ' + $Port +
    ' --data-dir "' + $dataDir + '"' +
    ' --log-dir "' + $logDir + '"'

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne "Stopped") {
        Stop-Service -Name $ServiceName -Force -ErrorAction Stop
        Start-Sleep -Seconds 2
    }

    Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName" -Name ImagePath -Value $imagePath
    Set-Service -Name $ServiceName -StartupType Automatic
} else {
    New-Service -Name $ServiceName -BinaryPathName $imagePath -DisplayName $DisplayName -StartupType Automatic | Out-Null
}

& sc.exe description $ServiceName "Runs the local Webkassa sidecar for iikoFront on this terminal." | Out-Null
& sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null
& sc.exe failureflag $ServiceName 1 | Out-Null
Start-Service -Name $ServiceName
Start-Sleep -Seconds 3
Get-Service -Name $ServiceName
