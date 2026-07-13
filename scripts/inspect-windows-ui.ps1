param(
    [string]$OutFile = "C:\OpenClaw\logs\webkassa-iiko-ui-tree.txt",
    [int]$MaxDepth = 4
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName UIAutomationClient

$lines = New-Object System.Collections.Generic.List[string]

function Add-Element {
    param(
        [System.Windows.Automation.AutomationElement]$Element,
        [int]$Depth
    )

    if ($null -eq $Element -or $Depth -gt $MaxDepth) {
        return
    }

    $current = $Element.Current
    $indent = "  " * $Depth
    $name = ($current.Name -replace "`r|`n", " ").Trim()
    $className = ($current.ClassName -replace "`r|`n", " ").Trim()
    $automationId = ($current.AutomationId -replace "`r|`n", " ").Trim()
    $controlType = $current.ControlType.ProgrammaticName

    if ($name -or $className -or $automationId) {
        $lines.Add(("{0}{1} | pid={2} | class={3} | id={4} | name={5}" -f $indent, $controlType, $current.ProcessId, $className, $automationId, $name))
    }

    $children = $Element.FindAll(
        [System.Windows.Automation.TreeScope]::Children,
        [System.Windows.Automation.Condition]::TrueCondition
    )

    foreach ($child in $children) {
        Add-Element -Element $child -Depth ($Depth + 1)
    }
}

$root = [System.Windows.Automation.AutomationElement]::RootElement
$windows = $root.FindAll(
    [System.Windows.Automation.TreeScope]::Children,
    [System.Windows.Automation.Condition]::TrueCondition
)

$lines.Add("CapturedAt=$(Get-Date -Format 'dd-MM-yyyy HH:mm:ss')")
$lines.Add("MaxDepth=$MaxDepth")

foreach ($window in $windows) {
    Add-Element -Element $window -Depth 0
}

$dir = Split-Path -Parent $OutFile
if ($dir -and -not (Test-Path -LiteralPath $dir)) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}

$lines | Set-Content -LiteralPath $OutFile -Encoding UTF8
Write-Output $OutFile
