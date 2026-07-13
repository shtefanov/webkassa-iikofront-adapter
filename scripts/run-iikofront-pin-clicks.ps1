param(
    [int] $X = 936,
    [int] $Y = 347,
    [int] $Clicks = 4
)

$ErrorActionPreference = "Stop"

$logPath = "C:\OpenClaw\logs\webkassa-pin-clicks.log"
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $logPath) | Out-Null

try {
    "Started $(Get-Date -Format 'dd-MM-yyyy HH:mm:ss') X=$X Y=$Y Clicks=$Clicks User=$([Security.Principal.WindowsIdentity]::GetCurrent().Name) Session=$((Get-Process -Id $PID).SessionId)" |
        Out-File -Encoding UTF8 -FilePath $logPath -Append
    & "C:\OpenClaw\work\webkassa\scripts\focus-window-by-process.ps1" -ProcessName "Resto.Front.Main" *>&1 |
        Out-File -Encoding UTF8 -FilePath $logPath -Append
    Start-Sleep -Milliseconds 300
    & "C:\OpenClaw\work\webkassa\scripts\click-windows-point-only.ps1" -X $X -Y $Y -Clicks $Clicks *>&1 |
        Out-File -Encoding UTF8 -FilePath $logPath -Append
    "Finished $(Get-Date -Format 'dd-MM-yyyy HH:mm:ss')" |
        Out-File -Encoding UTF8 -FilePath $logPath -Append
} catch {
    "ERROR: $($_.Exception.Message)" | Out-File -Encoding UTF8 -FilePath $logPath -Append
    throw
}
