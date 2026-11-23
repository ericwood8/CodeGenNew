param([Parameter(Mandatory)][string]$Exe, [string]$Menu = 'Stock Ledger')
# Clicks through the list screen of a stock ledger (StockTables=StockLevel) in a generated WinUI3 or Uno (Windows target) app over a scratch copy of Schemas/stock.sql, with UI Automation:
#   python stock_desktop_seed.py <scratch sqlite file>
#   powershell -File winui3_stock_check.ps1 -Exe <app.exe>      (the Uno app needs the API running and API_BASE_URL set)
# Checks that the screen is read only (no Add New) and that Show stock answers what an item has, Stock-take writes the count (and refuses one below what is reserved) and Low stock lists the item under its reorder level.
Import-Module C:\Github\CodeGenNew\Docs\Verification\UiAutomation.psm1 -Force -WarningAction Ignore
$ErrorActionPreference = 'Stop'

function Set-Box([string]$name, [string]$value) {
    $box = Find-Name $name
    $box.SetFocus()
    Start-Sleep -Milliseconds 400
    Set-Text $box $value
}

function Press([string]$caption) {
    (Find-Name $caption).SetFocus()   # a text box writes its value back when it loses focus
    Invoke-El (Find-Name $caption)
    Start-Sleep -Seconds 3
    $all = (List-Names | Select-Object -First 600) -join ' | '
    if ($all -match 'Text: ((Give|There|Counted|The |Low:|Nothing|\d+ available)[^|]*)') { $Matches[1].Trim() } else { $all }
}

Start-App -Path $Exe | Out-Null
try {
    Start-Sleep -Seconds 5
    Select-El (Find-Name $Menu)
    Start-Sleep -Seconds 4
    $page = (List-Names | Select-Object -First 600) -join ' | '
    Assert-That (-not $page.Contains('Add New')) "a ledger has no Add New button"
    Set-Box 'Stock item' ' 1 '
    $answer = Press 'Show stock'
    Assert-That ($answer -match '6 available \(10 on hand, 4 reserved\), low') "item 1 has 6 available and is low ($answer)"
    Set-Box 'Stock item' '99'
    Assert-That ((Press 'Show stock').Contains('There is no such item.')) "an item that does not exist has none"
    Set-Box 'Stock item' '1'
    Set-Box 'Stock counted' '2'
    Assert-That ((Press 'Stock-take').Contains('The count was refused')) "a count below what is reserved is refused"
    Set-Box 'Stock counted' '9'
    $answer = Press 'Stock-take'
    Assert-That ($answer -match 'Counted 9 \(-1\), 5 available') "a count of 9 is an adjustment of -1 ($answer)"
    $answer = Press 'Low stock'
    Assert-That ($answer -match 'Low: 1 \(5\)') "item 1 is on the low list ($answer)"
} finally { Stop-App }
if ($script:Failed) { exit 1 }
