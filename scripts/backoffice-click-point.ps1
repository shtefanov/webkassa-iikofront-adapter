param(
    [Parameter(Mandatory = $true)]
    [int] $X,

    [Parameter(Mandatory = $true)]
    [int] $Y,

    [int] $Clicks = 1,
    [string] $ScreenshotPath = "C:\OpenClaw\logs\webkassa-iiko-screen.png"
)

$ErrorActionPreference = "Stop"

Add-Type @"
using System;
using System.Runtime.InteropServices;

public class BackOfficeClickInput
{
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint data, UIntPtr extra);
}
"@

[BackOfficeClickInput]::SetCursorPos($X,$Y) | Out-Null
Start-Sleep -Milliseconds 100
for ($i = 0; $i -lt $Clicks; $i++) {
    [BackOfficeClickInput]::mouse_event(0x0002,0,0,0,[UIntPtr]::Zero)
    Start-Sleep -Milliseconds 80
    [BackOfficeClickInput]::mouse_event(0x0004,0,0,0,[UIntPtr]::Zero)
    Start-Sleep -Milliseconds 250
}

Start-Sleep -Seconds 1
& "C:\OpenClaw\work\webkassa\scripts\run-capture-windows-screen.ps1" -OutPath $ScreenshotPath
