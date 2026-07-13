param(
    [int] $WheelSteps = -10,
    [string] $ScreenshotPath = "C:\OpenClaw\logs\webkassa-iiko-screen.png"
)

$ErrorActionPreference = "Stop"

$arguments = "-NoProfile -ExecutionPolicy Bypass -File ""C:\OpenClaw\work\webkassa\scripts\backoffice-clear-search-and-scroll.ps1"" -WheelSteps $WheelSteps -ScreenshotPath ""$ScreenshotPath"""

& "C:\OpenClaw\work\webkassa\scripts\start-process-in-active-session.ps1" `
    -ExePath "C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe" `
    -WorkingDirectory "C:\OpenClaw\work\webkassa" `
    -Arguments $arguments
