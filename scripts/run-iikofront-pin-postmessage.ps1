param(
    [int] $X = 748,
    [int] $Y = 184,
    [int] $Clicks = 4
)

$ErrorActionPreference = "Stop"

$logPath = "C:\OpenClaw\logs\webkassa-post-click.log"
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $logPath) | Out-Null

Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class WebkassaPostClick
{
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    public static extern bool ScreenToClient(IntPtr hWnd, ref POINT point);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    public static IntPtr LParam(int x, int y)
    {
        return (IntPtr)((y << 16) | (x & 0xffff));
    }
}
"@

& "C:\OpenClaw\work\webkassa\scripts\focus-window-by-process.ps1" -ProcessName "Resto.Front.Main" *>&1 |
    Out-File -Encoding UTF8 -FilePath $logPath -Append

$process = Get-Process -Name "Resto.Front.Main" |
    Where-Object { $_.SessionId -ne 0 } |
    Select-Object -First 1

if (-not $process) {
    throw "Resto.Front.Main not found in interactive session"
}

$handle = $process.MainWindowHandle
if ($handle -eq [IntPtr]::Zero) {
    throw "Resto.Front.Main has no main window handle"
}

"Started $(Get-Date -Format 'dd-MM-yyyy HH:mm:ss') X=$X Y=$Y Clicks=$Clicks User=$([Security.Principal.WindowsIdentity]::GetCurrent().Name) Session=$((Get-Process -Id $PID).SessionId) hwnd=$handle" |
    Out-File -Encoding UTF8 -FilePath $logPath -Append

for ($index = 0; $index -lt $Clicks; $index++) {
    $point = New-Object WebkassaPostClick+POINT
    $point.X = $X
    $point.Y = $Y
    [WebkassaPostClick]::ScreenToClient($handle, [ref]$point) | Out-Null
    $lParam = [WebkassaPostClick]::LParam($point.X, $point.Y)
    "click screen=$X,$Y client=$($point.X),$($point.Y)" |
        Out-File -Encoding UTF8 -FilePath $logPath -Append
    [WebkassaPostClick]::PostMessage($handle, 0x0201, [IntPtr]1, $lParam) | Out-Null
    Start-Sleep -Milliseconds 80
    [WebkassaPostClick]::PostMessage($handle, 0x0202, [IntPtr]0, $lParam) | Out-Null
    Start-Sleep -Milliseconds 250
}

"Finished $(Get-Date -Format 'dd-MM-yyyy HH:mm:ss')" |
    Out-File -Encoding UTF8 -FilePath $logPath -Append
