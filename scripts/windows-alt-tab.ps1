$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.SendKeys]::SendWait("%{TAB}")
Start-Sleep -Seconds 1
& "C:\OpenClaw\work\webkassa\scripts\run-capture-windows-screen.ps1" -OutPath "C:\OpenClaw\logs\webkassa-iiko-screen.png"
