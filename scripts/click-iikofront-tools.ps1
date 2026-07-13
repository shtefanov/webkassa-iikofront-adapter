$ErrorActionPreference = "Stop"

$code = @"
using System;
using System.Runtime.InteropServices;

public static class MouseClicker
{
    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    public const uint LeftDown = 0x0002;
    public const uint LeftUp = 0x0004;

    public static void Click(int x, int y)
    {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(120);
        mouse_event(LeftDown, 0, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(80);
        mouse_event(LeftUp, 0, 0, 0, UIntPtr.Zero);
    }
}
"@

Add-Type $code
[MouseClicker]::Click(940, 755)
