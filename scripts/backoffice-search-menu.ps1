param(
    [Parameter(Mandatory = $true)]
    [string] $Term,

    [string] $ScreenshotPath = "C:\OpenClaw\logs\webkassa-iiko-screen.png",
    [string] $UiPath = "C:\OpenClaw\logs\backoffice-search-ui.txt"
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;

public class BackOfficeSearchInput
{
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint data, UIntPtr extra);
}
"@

[BackOfficeSearchInput]::SetCursorPos(182,45) | Out-Null
Start-Sleep -Milliseconds 100
[BackOfficeSearchInput]::mouse_event(0x0002,0,0,0,[UIntPtr]::Zero)
Start-Sleep -Milliseconds 80
[BackOfficeSearchInput]::mouse_event(0x0004,0,0,0,[UIntPtr]::Zero)
Start-Sleep -Milliseconds 250

[System.Windows.Forms.SendKeys]::SendWait("^a")
Start-Sleep -Milliseconds 100
Set-Clipboard -Value $Term
Start-Sleep -Milliseconds 100
[System.Windows.Forms.SendKeys]::SendWait("^v")
Start-Sleep -Seconds 2

& "C:\OpenClaw\work\webkassa\scripts\run-capture-windows-screen.ps1" -OutPath $ScreenshotPath
& "C:\OpenClaw\work\webkassa\scripts\inspect-windows-ui.ps1" -OutFile $UiPath -MaxDepth 10
