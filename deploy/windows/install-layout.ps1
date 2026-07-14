param(
    [string]$Root = "$env:ProgramData\WebkassaIikoFrontAdapter"
)

$ErrorActionPreference = "Stop"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this layout helper from an elevated PowerShell session."
}

$directories = @(
    $Root,
    (Join-Path $Root "config"),
    (Join-Path $Root "secrets"),
    (Join-Path $Root "secrets\ipc"),
    (Join-Path $Root "sidecar"),
    (Join-Path $Root "logs"),
    (Join-Path $Root "support-bundles"),
    (Join-Path $Root "backups")
)

foreach ($directory in $directories) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

& icacls $Root /inheritance:r /T /C | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Failed to remove inherited permissions from $Root." }
foreach ($untrustedSid in @('*S-1-1-0', '*S-1-5-11', '*S-1-5-32-545')) {
    & icacls $Root /remove:g $untrustedSid /T /C | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to remove $untrustedSid permissions from $Root." }
}
& icacls $Root /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' /T /C | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Failed to protect $Root." }

Write-Host "Webkassa iikoFront adapter layout prepared under $Root"
Write-Host "Copy config to: $(Join-Path $Root 'config\webkassa-adapter.config.json')"
Write-Host "Protected secrets directory: $(Join-Path $Root 'secrets')"
Write-Host "For an actual terminal install, use scripts\install-iikofront-terminal.ps1 so the iikoFront identity receives only the required per-directory access."
