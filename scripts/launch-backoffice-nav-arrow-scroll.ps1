param(
    [int] $Clicks = 12,
    [string] $Direction = "Down",
    [string] $ScreenshotPath = "C:\OpenClaw\logs\webkassa-iiko-screen.png"
)

$ErrorActionPreference = "Stop"

$arguments = "-NoProfile -ExecutionPolicy Bypass -File ""C:\OpenClaw\work\webkassa\scripts\backoffice-nav-arrow-scroll.ps1"" -Clicks $Clicks -Direction ""$Direction"" -ScreenshotPath ""$ScreenshotPath"""

& "C:\OpenClaw\work\webkassa\scripts\start-process-in-active-session.ps1" `
    -ExePath "C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe" `
    -WorkingDirectory "C:\OpenClaw\work\webkassa" `
    -Arguments $arguments
