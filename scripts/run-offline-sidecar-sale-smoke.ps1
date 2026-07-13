param(
    [string]$BaseUrl = "http://127.0.0.1:17777",
    [string]$OutDir = "docs\smoke-tests",
    [string]$FirewallRuleName = "OpenClaw Webkassa offline smoke block node 443"
)

$ErrorActionPreference = "Stop"

function Get-Json {
    param([Parameter(Mandatory = $true)][string]$Path)
    Invoke-RestMethod -Method Get -Uri ($BaseUrl.TrimEnd("/") + $Path)
}

function Post-Json {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Body
    )

    $json = $Body | ConvertTo-Json -Depth 30
    Invoke-RestMethod -Method Post -Uri ($BaseUrl.TrimEnd("/") + $Path) -ContentType "application/json" -Body $json
}

function Remove-OfflineFirewallRule {
    Get-NetFirewallRule -DisplayName $FirewallRuleName -ErrorAction SilentlyContinue |
        Remove-NetFirewallRule
}

$stamp = (Get-Date).ToUniversalTime().ToString("yyyyMMddHHmmss")
$statusBefore = Get-Json "/status"
$offlineBefore = Get-Json "/offline/status"
$pendingBefore = [int]($offlineBefore.offlineQueue.pending)

if ($pendingBefore -ne 0) {
    throw "Offline queue is not empty before smoke: pending=$pendingBefore"
}

$nodePath = (Get-Command node.exe -ErrorAction Stop).Source
$saleDraft = Get-Content "tests\fixtures\iiko\sale-draft.json" -Raw | ConvertFrom-Json
$saleDraft.orderId = "iiko-order-offline-$stamp"
$saleDraft.orderNumber = "OFF-$stamp"
$saleDraft.paymentId = "iiko-payment-offline-$stamp"
$saleDraft.operationTime = (Get-Date).ToString("o")
$saleDraft.positions[0].name = "Offline sidecar smoke sale $stamp"
$saleDraft.positions[0].code = "OFFLINE-$stamp"
$saleDraft.positions[0].productId = "product-offline-$stamp"

$queued = $null
$offlineDuringBlock = $null
$syncResult = $null
$offlineAfterSync = $null
$printFormat = $null
$blockStartedAt = (Get-Date).ToUniversalTime().ToString("o")

try {
    Remove-OfflineFirewallRule
    New-NetFirewallRule `
        -DisplayName $FirewallRuleName `
        -Direction Outbound `
        -Action Block `
        -Program $nodePath `
        -Protocol TCP `
        -RemotePort 443 `
        -Profile Any | Out-Null

    Start-Sleep -Seconds 2

    $queued = Post-Json "/fiscalize/sale" @{
        draft = $saleDraft
        runtime = @{
            allowOffline = $true
        }
    }

    if ($queued.status -ne "queued_offline" -or -not $queued.queuedOffline) {
        throw "Expected queued_offline response, got status=$($queued.status)"
    }

    $offlineDuringBlock = Get-Json "/offline/status"
}
finally {
    Remove-OfflineFirewallRule
}

Start-Sleep -Seconds 3
$syncResult = Post-Json "/offline/sync" @{
    runtime = @{}
}
$offlineAfterSync = Get-Json "/offline/status"

if ($queued -and $queued.externalCheckNumber) {
    $printFormat = Post-Json "/tickets/print-format" @{
        externalCheckNumber = $queued.externalCheckNumber
        runtime = @{}
    }
}

$lineTypes = @{}
if ($printFormat -and $printFormat.lines) {
    foreach ($line in $printFormat.lines) {
        $key = [string]$line.type
        if (-not $lineTypes.ContainsKey($key)) {
            $lineTypes[$key] = 0
        }
        $lineTypes[$key]++
    }
}

$report = [ordered]@{
    generatedAt = (Get-Date).ToUniversalTime().ToString("o")
    mode = "windows-local-sidecar-offline-sale-smoke"
    firewall = [ordered]@{
        ruleName = $FirewallRuleName
        nodePath = $nodePath
        blockedProtocol = "TCP"
        blockedRemotePort = 443
        startedAt = $blockStartedAt
        removed = $true
    }
    sidecar = [ordered]@{
        statusOk = $statusBefore.ok
        version = $statusBefore.version
        protocolVersion = $statusBefore.protocolVersion
        pendingBefore = $pendingBefore
    }
    queuedSale = [ordered]@{
        status = $queued.status
        queuedOffline = $queued.queuedOffline
        externalCheckNumber = $queued.externalCheckNumber
        offlineExpiresAt = $queued.offlineExpiresAt
    }
    offlineDuringBlock = $offlineDuringBlock.offlineQueue
    sync = [ordered]@{
        ok = $syncResult.ok
        status = $syncResult.status
        synced = $syncResult.synced
        failed = $syncResult.failed
        offlineQueue = $syncResult.offlineQueue
    }
    offlineAfterSync = $offlineAfterSync.offlineQueue
    printFormat = [ordered]@{
        ok = $printFormat.ok
        externalCheckNumber = $printFormat.externalCheckNumber
        lineCount = @($printFormat.lines).Count
        lineTypes = $lineTypes
    }
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$outPath = Join-Path $OutDir "$((Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH-mm-ss-fffZ"))_windows-local-sidecar-offline-sale-smoke.json"
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($outPath, (($report | ConvertTo-Json -Depth 30) + [Environment]::NewLine), $utf8NoBom)

$report | ConvertTo-Json -Depth 30
Write-Host "REPORT=$outPath"
