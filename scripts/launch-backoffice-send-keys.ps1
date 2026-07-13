param(
    [Parameter(Mandatory = $true)]
    [string] $Keys,

    [string] $ScreenshotPath = "C:\OpenClaw\logs\webkassa-iiko-screen.png"
)

$ErrorActionPreference = "Stop"

$escapedKeys = $Keys.Replace('"', '\"')
$escapedScreenshotPath = $ScreenshotPath.Replace('"', '\"')
$arguments = "-NoProfile -ExecutionPolicy Bypass -File ""C:\OpenClaw\work\webkassa\scripts\backoffice-send-keys.ps1"" -Keys ""$escapedKeys"" -ScreenshotPath ""$escapedScreenshotPath"""

& "C:\OpenClaw\work\webkassa\scripts\start-process-in-active-session.ps1" `
    -ExePath "C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe" `
    -WorkingDirectory "C:\OpenClaw\work\webkassa" `
    -Arguments $arguments
