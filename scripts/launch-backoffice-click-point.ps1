param(
    [Parameter(Mandatory = $true)]
    [int] $X,

    [Parameter(Mandatory = $true)]
    [int] $Y,

    [int] $Clicks = 1,
    [string] $ScreenshotPath = "C:\OpenClaw\logs\webkassa-iiko-screen.png"
)

$ErrorActionPreference = "Stop"

$arguments = "-NoProfile -ExecutionPolicy Bypass -File ""C:\OpenClaw\work\webkassa\scripts\backoffice-click-point.ps1"" -X $X -Y $Y -Clicks $Clicks -ScreenshotPath ""$ScreenshotPath"""

& "C:\OpenClaw\work\webkassa\scripts\start-process-in-active-session.ps1" `
    -ExePath "C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe" `
    -WorkingDirectory "C:\OpenClaw\work\webkassa" `
    -Arguments $arguments
