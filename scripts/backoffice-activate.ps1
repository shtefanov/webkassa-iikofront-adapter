$ErrorActionPreference = "Stop"

Add-Type @"
using System;
using System.Runtime.InteropServices;

public class BackOfficeWindowActivator
{
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
"@

$process = Get-Process BackOffice -ErrorAction Stop | Sort-Object StartTime | Select-Object -First 1
if ($process.MainWindowHandle -eq [IntPtr]::Zero) {
    throw "BackOffice main window handle is zero"
}

[BackOfficeWindowActivator]::ShowWindow($process.MainWindowHandle, 9) | Out-Null
Start-Sleep -Milliseconds 200
[BackOfficeWindowActivator]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
Start-Sleep -Seconds 1

& "C:\OpenClaw\work\webkassa\scripts\run-capture-windows-screen.ps1" -OutPath "C:\OpenClaw\logs\webkassa-iiko-screen.png"
