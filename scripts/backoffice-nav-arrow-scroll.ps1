param(
    [int] $Clicks = 12,
    [string] $Direction = "Down",
    [string] $ScreenshotPath = "C:\OpenClaw\logs\webkassa-iiko-screen.png"
)

$ErrorActionPreference = "Stop"

Add-Type @"
using System;
using System.Runtime.InteropServices;

public class BackOfficeNavArrowInput
{
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint data, UIntPtr extra);
}
"@

$x = 132
$y = if ($Direction -eq "Up") { 72 } else { 712 }
[BackOfficeNavArrowInput]::SetCursorPos($x,$y) | Out-Null
Start-Sleep -Milliseconds 100
for ($i = 0; $i -lt $Clicks; $i++) {
    [BackOfficeNavArrowInput]::mouse_event(0x0002,0,0,0,[UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [BackOfficeNavArrowInput]::mouse_event(0x0004,0,0,0,[UIntPtr]::Zero)
    Start-Sleep -Milliseconds 180
}

Start-Sleep -Seconds 1
& "C:\OpenClaw\work\webkassa\scripts\run-capture-windows-screen.ps1" -OutPath $ScreenshotPath
