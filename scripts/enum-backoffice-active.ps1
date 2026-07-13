$ErrorActionPreference = "Continue"

$outFile = "C:\OpenClaw\logs\backoffice-enum.txt"
"started $(Get-Date -Format 'dd-MM-yyyy HH:mm:ss')" | Set-Content $outFile -Encoding UTF8

Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;

public class WinEnum3
{
    public delegate bool EnumWindowProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr hwndParent, EnumWindowProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
"@

$processes = Get-Process BackOffice -ErrorAction SilentlyContinue | Sort-Object StartTime
foreach ($process in $processes) {
    "PROC id=$($process.Id) title=$($process.MainWindowTitle) hwnd=$($process.MainWindowHandle)" | Add-Content $outFile -Encoding UTF8
    $script:targetPid = $process.Id
    $script:tops = @()

    [WinEnum3]::EnumWindows({
        param($handle, $param)
        [uint32]$windowPid = 0
        [WinEnum3]::GetWindowThreadProcessId($handle, [ref]$windowPid) | Out-Null
        if ($windowPid -eq $script:targetPid) {
            $script:tops += $handle
        }
        return $true
    }, [IntPtr]::Zero) | Out-Null

    foreach ($top in $script:tops) {
        $text = New-Object System.Text.StringBuilder 512
        $class = New-Object System.Text.StringBuilder 256
        $rect = New-Object WinEnum3+RECT
        [WinEnum3]::GetWindowText($top, $text, $text.Capacity) | Out-Null
        [WinEnum3]::GetClassName($top, $class, $class.Capacity) | Out-Null
        [WinEnum3]::GetWindowRect($top, [ref]$rect) | Out-Null
        "TOP hwnd=$top class=$($class.ToString()) text=$($text.ToString()) rect=$($rect.Left),$($rect.Top),$($rect.Right),$($rect.Bottom)" | Add-Content $outFile -Encoding UTF8

        $script:index = 0
        [WinEnum3]::EnumChildWindows($top, {
            param($handle, $param)
            $script:index++
            $text = New-Object System.Text.StringBuilder 512
            $class = New-Object System.Text.StringBuilder 256
            $rect = New-Object WinEnum3+RECT
            [WinEnum3]::GetWindowText($handle, $text, $text.Capacity) | Out-Null
            [WinEnum3]::GetClassName($handle, $class, $class.Capacity) | Out-Null
            [WinEnum3]::GetWindowRect($handle, [ref]$rect) | Out-Null
            ("{0,3} hwnd={1} class={2} text={3} rect={4},{5},{6},{7}" -f $script:index,$handle,$class.ToString(),$text.ToString(),$rect.Left,$rect.Top,$rect.Right,$rect.Bottom) | Add-Content $outFile -Encoding UTF8
            return $true
        }, [IntPtr]::Zero) | Out-Null
    }
}

& "C:\OpenClaw\work\webkassa\scripts\run-capture-windows-screen.ps1" -OutPath "C:\OpenClaw\logs\backoffice-enum-screen.png"
