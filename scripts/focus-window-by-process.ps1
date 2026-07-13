param(
    [Parameter(Mandatory = $true)]
    [string] $ProcessName
)

$ErrorActionPreference = "Stop"

$code = @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class WebkassaWindowFocus
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr SetActiveWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr SetFocus(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    public static IntPtr FindMainWindow(int processId)
    {
        IntPtr result = IntPtr.Zero;
        EnumWindows(delegate(IntPtr hWnd, IntPtr lParam) {
            if (!IsWindowVisible(hWnd)) return true;
            uint windowProcessId;
            GetWindowThreadProcessId(hWnd, out windowProcessId);
            if (windowProcessId != processId) return true;
            result = hWnd;
            return false;
        }, IntPtr.Zero);
        return result;
    }

    public static string GetTitle(IntPtr hWnd)
    {
        var text = new StringBuilder(512);
        GetWindowText(hWnd, text, text.Capacity);
        return text.ToString();
    }

    public static bool ForceFocus(IntPtr hWnd)
    {
        IntPtr foreground = GetForegroundWindow();
        uint ignored;
        uint foregroundThread = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, out ignored);
        uint targetThread = GetWindowThreadProcessId(hWnd, out ignored);
        uint currentThread = GetCurrentThreadId();

        if (foregroundThread != 0) AttachThreadInput(currentThread, foregroundThread, true);
        if (targetThread != 0) AttachThreadInput(currentThread, targetThread, true);
        try
        {
            ShowWindow(hWnd, 9);
            BringWindowToTop(hWnd);
            SetActiveWindow(hWnd);
            SetFocus(hWnd);
            return SetForegroundWindow(hWnd);
        }
        finally
        {
            if (targetThread != 0) AttachThreadInput(currentThread, targetThread, false);
            if (foregroundThread != 0) AttachThreadInput(currentThread, foregroundThread, false);
        }
    }
}
"@

Add-Type $code

$process = Get-Process -Name $ProcessName -ErrorAction Stop |
    Where-Object { $_.SessionId -ne 0 } |
    Select-Object -First 1

if (-not $process) {
    throw "Process not found in interactive session: $ProcessName"
}

$handle = [WebkassaWindowFocus]::FindMainWindow($process.Id)
if ($handle -eq [IntPtr]::Zero) {
    throw "No visible top-level window found for $ProcessName pid=$($process.Id)"
}

[WebkassaWindowFocus]::ForceFocus($handle) | Out-Null
Start-Sleep -Milliseconds 300

Write-Host "Focused $ProcessName pid=$($process.Id) hwnd=$handle title='$([WebkassaWindowFocus]::GetTitle($handle))'"
