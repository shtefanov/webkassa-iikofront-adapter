param(
    [string]$IikoRoot = "C:\Program Files\iiko\iikoRMS\Front.Net"
)

$ErrorActionPreference = "Stop"

$exePath = Join-Path $IikoRoot "Resto.Front.Main.exe"
if (!(Test-Path $exePath)) {
    throw "iikoFront executable not found: $exePath"
}

Start-Process -FilePath $exePath -WorkingDirectory $IikoRoot
