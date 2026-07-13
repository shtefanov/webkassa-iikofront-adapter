$ErrorActionPreference = "Stop"

$sequencePath = "C:\OpenClaw\work\webkassa\scripts\click-windows-points.json"
$points = Get-Content -LiteralPath $sequencePath -Raw | ConvertFrom-Json

$code = @"
using System;
using System.Runtime.InteropServices;

public static class MouseClicker
{
    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr extraInfo);

    public static void Click(int x, int y)
    {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(120);
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(80);
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
    }
}
"@

Add-Type $code

foreach ($point in $points) {
    $delayMs = 300
    if ($null -ne $point.delayMs) {
        $delayMs = [int]$point.delayMs
    }

    [MouseClicker]::Click([int]$point.x, [int]$point.y)
    Start-Sleep -Milliseconds $delayMs
}
