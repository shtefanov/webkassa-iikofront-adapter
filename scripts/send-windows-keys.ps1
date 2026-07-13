param(
    [Parameter(Mandatory=$true)]
    [string]$Keys,
    [int]$DelayMs = 150
)

$ErrorActionPreference = "Stop"

$code = @"
using System;
using System.Runtime.InteropServices;

public static class WebkassaKeySender
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

Add-Type $code

foreach ($char in $Keys.ToCharArray()) {
    $codePoint = [int][char]$char
    if ($codePoint -ge 48 -and $codePoint -le 57) {
        [WebkassaKeySender]::Press([byte]$codePoint)
    } elseif ($codePoint -eq 13) {
        [WebkassaKeySender]::Press([byte]13)
    } else {
        throw "Unsupported key character: $char"
    }

    Start-Sleep -Milliseconds $DelayMs
}
