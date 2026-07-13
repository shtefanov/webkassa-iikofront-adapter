param(
    [Parameter(Mandatory = $true)]
    [string] $Keys,

    [string] $ScreenshotPath = "C:\OpenClaw\logs\webkassa-iiko-screen.png"
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.SendKeys]::SendWait($Keys)
Start-Sleep -Seconds 1
& "C:\OpenClaw\work\webkassa\scripts\run-capture-windows-screen.ps1" -OutPath $ScreenshotPath
