param(
    [string] $OutPath = "C:\OpenClaw\logs\webkassa-iiko-screen.png"
)

$ErrorActionPreference = "Stop"

Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class WebkassaConsoleWindow
{
    [DllImport("kernel32.dll")] public static extern IntPtr GetConsoleWindow();
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
"@

$console = [WebkassaConsoleWindow]::GetConsoleWindow()
if ($console -ne [IntPtr]::Zero) {
    [WebkassaConsoleWindow]::ShowWindow($console, 6) | Out-Null
    Start-Sleep -Milliseconds 500
}

& "C:\OpenClaw\work\webkassa\scripts\capture-windows-screen.ps1" -OutPath $OutPath
