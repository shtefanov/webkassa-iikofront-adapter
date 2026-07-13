param(
    [int] $WheelSteps = -10,
    [string] $ScreenshotPath = "C:\OpenClaw\logs\webkassa-iiko-screen.png"
)

$ErrorActionPreference = "Stop"

Add-Type @"
using System;
using System.Runtime.InteropServices;

public class BackOfficeNavInput
{
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint data, UIntPtr extra);
}
"@
Add-Type -AssemblyName System.Windows.Forms

# Clear menu search using keyboard selection; the clear button is not stable
# across filtered and unfiltered states.
[BackOfficeNavInput]::SetCursorPos(80,75) | Out-Null
Start-Sleep -Milliseconds 100
[BackOfficeNavInput]::mouse_event(0x0002,0,0,0,[UIntPtr]::Zero)
Start-Sleep -Milliseconds 80
[BackOfficeNavInput]::mouse_event(0x0004,0,0,0,[UIntPtr]::Zero)
Start-Sleep -Milliseconds 300
[System.Windows.Forms.SendKeys]::SendWait("^a")
Start-Sleep -Milliseconds 100
[System.Windows.Forms.SendKeys]::SendWait("{BACKSPACE}")
Start-Sleep -Milliseconds 500

[BackOfficeNavInput]::SetCursorPos(130,700) | Out-Null
Start-Sleep -Milliseconds 100
for ($i = 0; $i -lt [Math]::Abs($WheelSteps); $i++) {
    $delta = if ($WheelSteps -lt 0) { -120 } else { 120 }
    [BackOfficeNavInput]::mouse_event(0x0800,0,0,$delta,[UIntPtr]::Zero)
    Start-Sleep -Milliseconds 120
}

Start-Sleep -Seconds 1
& "C:\OpenClaw\work\webkassa\scripts\run-capture-windows-screen.ps1" -OutPath $ScreenshotPath
