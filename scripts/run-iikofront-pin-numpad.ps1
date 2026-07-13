param(
    [string]$Pin = "1111",
    [int]$DelayMs = 150
)

$ErrorActionPreference = "Stop"

$logPath = "C:\OpenClaw\logs\webkassa-pin-numpad.log"
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $logPath) | Out-Null

$code = @"
using System;
using System.Runtime.InteropServices;

public static class WebkassaNumpadSender
{
    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, int dwFlags, int dwExtraInfo);

    public static void Press(byte key)
    {
        keybd_event(key, 0, 0, 0);
        System.Threading.Thread.Sleep(80);
        keybd_event(key, 0, 2, 0);
    }
}
"@

try {
    Add-Type $code
    & "C:\OpenClaw\work\webkassa\scripts\focus-window-by-process.ps1" -ProcessName "Resto.Front.Main" *>&1 |
        Out-File -Encoding UTF8 -FilePath $logPath

    foreach ($char in $Pin.ToCharArray()) {
        $digit = [int][char]$char - [int][char]'0'
        if ($digit -lt 0 -or $digit -gt 9) {
            throw "Unsupported PIN character: $char"
        }

        [WebkassaNumpadSender]::Press([byte](96 + $digit))
        Start-Sleep -Milliseconds $DelayMs
    }

    "OK" | Out-File -Encoding UTF8 -FilePath $logPath -Append
} catch {
    "ERROR: $($_.Exception.Message)" | Out-File -Encoding UTF8 -FilePath $logPath -Append
    throw
}
