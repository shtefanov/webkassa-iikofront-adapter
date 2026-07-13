param(
    [Parameter(Mandatory = $true)]
    [int] $X,

    [Parameter(Mandatory = $true)]
    [int] $Y,

    [int] $Clicks = 1
)

$ErrorActionPreference = "Stop"

Add-Type @"
using System;
using System.Runtime.InteropServices;

public class WebkassaClickOnlyInput
{
    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public static void Click()
    {
        INPUT down = new INPUT();
        down.type = 0;
        down.mi.dwFlags = 0x0002;
        INPUT up = new INPUT();
        up.type = 0;
        up.mi.dwFlags = 0x0004;
        INPUT[] inputs = new INPUT[] { down, up };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
    }
}
"@

[WebkassaClickOnlyInput]::SetCursorPos($X, $Y) | Out-Null
Start-Sleep -Milliseconds 100

for ($i = 0; $i -lt $Clicks; $i++) {
    [WebkassaClickOnlyInput]::Click()
    Start-Sleep -Milliseconds 180
}
