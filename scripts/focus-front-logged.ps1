$ErrorActionPreference = "Stop"

$logPath = "C:\OpenClaw\logs\webkassa-focus-front.log"
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $logPath) | Out-Null

try {
    & "C:\OpenClaw\work\webkassa\scripts\focus-window-by-process.ps1" -ProcessName "Resto.Front.Main" *>&1 |
        Out-File -Encoding UTF8 -FilePath $logPath
} catch {
    "ERROR: $($_.Exception.Message)" | Out-File -Encoding UTF8 -FilePath $logPath
    throw
}
