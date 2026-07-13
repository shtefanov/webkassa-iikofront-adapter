$ErrorActionPreference = "Stop"

& "C:\OpenClaw\work\webkassa\scripts\inspect-windows-ui.ps1" `
    -OutFile "C:\OpenClaw\logs\webkassa-iiko-ui-tree.txt" `
    -MaxDepth 5
