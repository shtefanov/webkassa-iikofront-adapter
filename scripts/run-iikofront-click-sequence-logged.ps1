$ErrorActionPreference = "Stop"

$logPath = "C:\OpenClaw\logs\webkassa-click-sequence.log"
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $logPath) | Out-Null

try {
    Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class WebkassaClickRunnerConsole
{
    [DllImport("kernel32.dll")] public static extern IntPtr GetConsoleWindow();
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
"@

    $console = [WebkassaClickRunnerConsole]::GetConsoleWindow()
    if ($console -ne [IntPtr]::Zero) {
        [WebkassaClickRunnerConsole]::ShowWindow($console, 6) | Out-Null
        Start-Sleep -Milliseconds 250
    }

    & "C:\OpenClaw\work\webkassa\scripts\focus-window-by-process.ps1" -ProcessName "Resto.Front.Main" *>&1 |
        Out-File -Encoding UTF8 -FilePath $logPath
    & "C:\OpenClaw\work\webkassa\scripts\click-windows-points.ps1" *>&1 |
        Out-File -Encoding UTF8 -FilePath $logPath -Append
    "OK" | Out-File -Encoding UTF8 -FilePath $logPath -Append
} catch {
    "ERROR: $($_.Exception.Message)" | Out-File -Encoding UTF8 -FilePath $logPath -Append
    throw
}
