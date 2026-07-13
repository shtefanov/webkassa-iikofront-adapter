param(
    [Parameter(Mandatory = $true)]
    [string] $Keys
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.SendKeys]::SendWait($Keys)
Start-Sleep -Milliseconds 500
