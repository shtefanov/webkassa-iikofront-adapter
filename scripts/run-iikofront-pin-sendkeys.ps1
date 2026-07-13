param(
    [string]$Pin = "5321"
)

$ErrorActionPreference = "Stop"

$logPath = "C:\OpenClaw\logs\webkassa-pin-sendkeys.log"
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $logPath) | Out-Null

try {
    & "C:\OpenClaw\work\webkassa\scripts\focus-window-by-process.ps1" -ProcessName "Resto.Front.Main" *>&1 |
        Out-File -Encoding UTF8 -FilePath $logPath

    $shell = New-Object -ComObject WScript.Shell
    Start-Sleep -Milliseconds 500
    $shell.SendKeys($Pin)
    Start-Sleep -Milliseconds 500
    "OK" | Out-File -Encoding UTF8 -FilePath $logPath -Append
} catch {
    "ERROR: $($_.Exception.Message)" | Out-File -Encoding UTF8 -FilePath $logPath -Append
    throw
}
