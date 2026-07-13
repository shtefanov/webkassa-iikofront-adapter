param(
    [Parameter(Mandatory = $true)]
    [string] $Term,

    [string] $ScreenshotPath = "C:\OpenClaw\logs\webkassa-iiko-screen.png",
    [string] $UiPath = "C:\OpenClaw\logs\backoffice-search-ui.txt"
)

$ErrorActionPreference = "Stop"

$escapedTerm = $Term.Replace('"', '\"')
$escapedScreenshotPath = $ScreenshotPath.Replace('"', '\"')
$escapedUiPath = $UiPath.Replace('"', '\"')
$arguments = "-NoProfile -ExecutionPolicy Bypass -File ""C:\OpenClaw\work\webkassa\scripts\backoffice-search-menu.ps1"" -Term ""$escapedTerm"" -ScreenshotPath ""$escapedScreenshotPath"" -UiPath ""$escapedUiPath"""

& "C:\OpenClaw\work\webkassa\scripts\start-process-in-active-session.ps1" `
    -ExePath "C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe" `
    -WorkingDirectory "C:\OpenClaw\work\webkassa" `
    -Arguments $arguments
