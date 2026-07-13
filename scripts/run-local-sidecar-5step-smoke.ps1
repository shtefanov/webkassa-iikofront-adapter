param(
    [string]$BaseUrl = "http://127.0.0.1:17777",
    [string]$OutDir = "docs\smoke-tests"
)

$ErrorActionPreference = "Stop"

function Post-Json {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Body
    )

    $json = $Body | ConvertTo-Json -Depth 30
    Invoke-RestMethod -Method Post -Uri ($BaseUrl.TrimEnd("/") + $Path) -ContentType "application/json" -Body $json
}

$stamp = (Get-Date).ToUniversalTime().ToString("yyyyMMddHHmmss")
$status = Invoke-RestMethod -Uri ($BaseUrl.TrimEnd("/") + "/status")

$saleDraft = Get-Content "tests\fixtures\iiko\sale-draft.json" -Raw | ConvertFrom-Json
$saleDraft.orderId = "iiko-order-sidecar-$stamp"
$saleDraft.orderNumber = "SC-$stamp"
$saleDraft.paymentId = "iiko-payment-sidecar-$stamp"
$saleDraft.operationTime = (Get-Date).ToString("o")
$saleDraft.positions[0].name = "Sidecar smoke sale $stamp"
$saleDraft.positions[0].code = "SIDECAR-$stamp"
$saleDraft.positions[0].productId = "product-sidecar-$stamp"

$returnDraft = Get-Content "tests\fixtures\iiko\return-draft.json" -Raw | ConvertFrom-Json
$returnDraft.orderId = $saleDraft.orderId
$returnDraft.orderNumber = $saleDraft.orderNumber
$returnDraft.paymentId = $saleDraft.paymentId
$returnDraft.refundId = "iiko-refund-sidecar-$stamp"
$returnDraft.operationTime = (Get-Date).AddMinutes(1).ToString("o")
$returnDraft.positions[0].name = $saleDraft.positions[0].name
$returnDraft.positions[0].code = $saleDraft.positions[0].code
$returnDraft.positions[0].productId = $saleDraft.positions[0].productId

$sale = Post-Json "/fiscalize/sale" @{
    draft = $saleDraft
    runtime = @{
        allowOffline = $false
    }
}

$saleReturn = Post-Json "/fiscalize/return" @{
    draft = $returnDraft
    runtime = @{
        allowOffline = $false
        originalSaleExternalCheckNumber = $sale.externalCheckNumber
    }
}

$xReport = Post-Json "/reports/x" @{
    runtime = @{}
}

$zReport = Post-Json "/reports/z" @{
    runtime = @{}
}

$report = [ordered]@{
    generatedAt = (Get-Date).ToUniversalTime().ToString("o")
    mode = "windows-local-sidecar-5-step"
    sidecar = [ordered]@{
        statusOk = $status.ok
        version = $status.version
        protocolVersion = $status.protocolVersion
        fiscalServiceConfigured = $status.fiscalServiceConfigured
    }
    sale = [ordered]@{
        status = $sale.status
        externalCheckNumber = $sale.externalCheckNumber
        checkNumber = $sale.checkNumber
        shiftNumber = $sale.shiftNumber
    }
    return = [ordered]@{
        status = $saleReturn.status
        externalCheckNumber = $saleReturn.externalCheckNumber
        checkNumber = $saleReturn.checkNumber
        shiftNumber = $saleReturn.shiftNumber
    }
    xReport = [ordered]@{
        status = $xReport.status
        reportType = $xReport.reportType
        reportNumber = $xReport.reportNumber
        shiftNumber = $xReport.shiftNumber
        documentCount = $xReport.documentCount
    }
    zReport = [ordered]@{
        status = $zReport.status
        reportType = $zReport.reportType
        reportNumber = $zReport.reportNumber
        shiftNumber = $zReport.shiftNumber
        documentCount = $zReport.documentCount
    }
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$outPath = Join-Path $OutDir "$((Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH-mm-ss-fffZ"))_windows-local-sidecar-5-step.json"
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($outPath, (($report | ConvertTo-Json -Depth 30) + [Environment]::NewLine), $utf8NoBom)

$report | ConvertTo-Json -Depth 30
Write-Host "REPORT=$outPath"
