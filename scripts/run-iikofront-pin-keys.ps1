param(
    [string]$Pin = "1111"
)

$ErrorActionPreference = "Stop"

$logPath = "C:\OpenClaw\logs\webkassa-pin-keys.log"
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $logPath) | Out-Null

try {
    & "C:\OpenClaw\work\webkassa\scripts\focus-window-by-process.ps1" -ProcessName "Resto.Front.Main" *>&1 |
        Out-File -Encoding UTF8 -FilePath $logPath
    & "C:\OpenClaw\work\webkassa\scripts\send-windows-keys.ps1" -Keys $Pin *>&1 |
        Out-File -Encoding UTF8 -FilePath $logPath -Append
    "OK" | Out-File -Encoding UTF8 -FilePath $logPath -Append
} catch {
    "ERROR: $($_.Exception.Message)" | Out-File -Encoding UTF8 -FilePath $logPath -Append
    throw
}
