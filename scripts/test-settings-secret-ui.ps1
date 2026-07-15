param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath,
    [string]$OutFile = "C:\OpenClaw\logs\webkassa-settings-secret-ui.json"
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName UIAutomationClient

function Get-ValuePatternText {
    param([System.Windows.Automation.AutomationElement]$Element)

    $pattern = $null
    if (-not $Element.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$pattern)) {
        return ""
    }

    return ([System.Windows.Automation.ValuePattern]$pattern).Current.Value
}

function Test-AllBulletCharacters {
    param([string]$Value)

    if ([string]::IsNullOrEmpty($Value)) {
        return $false
    }

    foreach ($character in $Value.ToCharArray()) {
        if ($character -ne [char]0x2022) {
            return $false
        }
    }

    return $true
}

if (-not (Test-Path -LiteralPath $ExecutablePath -PathType Leaf)) {
    throw "Settings executable not found."
}

$process = Start-Process -FilePath $ExecutablePath -ArgumentList "--gui" -PassThru
$window = $null

try {
    for ($attempt = 0; $attempt -lt 30 -and $null -eq $window; $attempt++) {
        Start-Sleep -Milliseconds 250
        $conditionArguments = @(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            "Настройки Webkassa")
        $condition = New-Object System.Windows.Automation.PropertyCondition -ArgumentList $conditionArguments
        $window = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            $condition)
    }

    if ($null -eq $window) {
        throw "Settings window was not found in the active Windows session."
    }

    $editConditionArguments = @(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Edit)
    $editCondition = New-Object System.Windows.Automation.PropertyCondition -ArgumentList $editConditionArguments
    $edits = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $editCondition)
    $namedEdits = @{}
    foreach ($edit in $edits) {
        $namedEdits[$edit.Current.Name] = $edit
    }

    if (-not $namedEdits.ContainsKey("Webkassa API key") -or
        -not $namedEdits.ContainsKey("Webkassa Login") -or
        -not $namedEdits.ContainsKey("Webkassa Password")) {
        throw "Settings window does not expose the expected named Webkassa credential fields."
    }

    $apiKeyDisplay = Get-ValuePatternText $namedEdits["Webkassa API key"]
    $loginDisplay = Get-ValuePatternText $namedEdits["Webkassa Login"]
    $passwordDisplay = Get-ValuePatternText $namedEdits["Webkassa Password"]

    $buttonConditionArguments = @(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    $buttonCondition = New-Object System.Windows.Automation.PropertyCondition -ArgumentList $buttonConditionArguments
    $buttons = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $buttonCondition)
    $buttonNames = @($buttons | ForEach-Object { $_.Current.Name })

    $textConditionArguments = @(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Text)
    $textCondition = New-Object System.Windows.Automation.PropertyCondition -ArgumentList $textConditionArguments
    $texts = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCondition)
    $configuredStatusCount = @($texts | Where-Object { $_.Current.Name -eq "Настроено" }).Count

    $result = [ordered]@{
        ok = $true
        windowFound = $true
        loginVisibleAndNonEmpty = -not [string]::IsNullOrWhiteSpace($loginDisplay)
        apiKeyMasked = -not [string]::IsNullOrEmpty($apiKeyDisplay) -and $apiKeyDisplay.Contains([char]0x2022)
        passwordMasked = Test-AllBulletCharacters $passwordDisplay
        showButtonCount = @($buttonNames | Where-Object { $_ -eq "Показать" }).Count
        editButtonCount = @($buttonNames | Where-Object { $_ -eq "Изменить" }).Count
        configuredStatusCount = $configuredStatusCount
    }

    $result.ok = $result.loginVisibleAndNonEmpty -and
        $result.apiKeyMasked -and
        $result.passwordMasked -and
        $result.showButtonCount -ge 2 -and
        $result.editButtonCount -ge 2 -and
        $result.configuredStatusCount -ge 3

    $directory = Split-Path -Parent $OutFile
    if ($directory -and -not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    $result | ConvertTo-Json | Set-Content -LiteralPath $OutFile -Encoding UTF8

    if (-not $result.ok) {
        throw "Settings secret UI validation failed. See the boolean-only result file."
    }
}
catch {
    $directory = Split-Path -Parent $OutFile
    if ($directory -and -not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    [ordered]@{
        ok = $false
        error = $_.Exception.Message
    } | ConvertTo-Json | Set-Content -LiteralPath $OutFile -Encoding UTF8
    throw
}
finally {
    if ($null -ne $window) {
        $windowPattern = $null
        if ($window.TryGetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern, [ref]$windowPattern)) {
            ([System.Windows.Automation.WindowPattern]$windowPattern).Close()
        }
    }

    Start-Sleep -Milliseconds 300
    if (-not $process.HasExited) {
        $process.CloseMainWindow() | Out-Null
    }
}
