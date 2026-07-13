$ErrorActionPreference = "Stop"

Add-Type @"
using System;
using System.Runtime.InteropServices;

public class WebkassaBackOfficeActivatorOnly
{
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
"@

$process = Get-Process BackOffice -ErrorAction Stop | Sort-Object StartTime | Select-Object -First 1
if ($process.MainWindowHandle -eq [IntPtr]::Zero) {
    throw "BackOffice main window handle is zero"
}

[WebkassaBackOfficeActivatorOnly]::ShowWindow($process.MainWindowHandle, 9) | Out-Null
Start-Sleep -Milliseconds 200
[WebkassaBackOfficeActivatorOnly]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 500
