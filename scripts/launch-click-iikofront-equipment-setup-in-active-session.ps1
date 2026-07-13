$ErrorActionPreference = "Stop"

& "C:\OpenClaw\work\webkassa\scripts\start-process-in-active-session.ps1" `
    -ExePath "C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe" `
    -WorkingDirectory "C:\OpenClaw\work\webkassa" `
    -Arguments "-NoProfile -ExecutionPolicy Bypass -File C:\OpenClaw\work\webkassa\scripts\click-iikofront-equipment-setup.ps1"
