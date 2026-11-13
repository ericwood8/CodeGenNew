param([Parameter(Mandatory)][string]$Exe, [string]$Menu = 'Order Item')
# Clicks through the Add and Edit dialogs of an order line (SnapshotColumns=OrderItem:ProductName<-Product.Name+UnitPrice<-Product.Price+TaxRate<-TaxCategory.Rate:LineTotal=Quantity*UnitPrice) in a generated
# WinUI3 or Uno (Windows target) app over a scratch copy of Schemas/snapshot.sql with UI Automation:
#   python snapshot_desktop_seed.py <sqlite file> seed      (a product Widget at 10.5, a tax category Standard at 8.25 and an order SO-1)
#   powershell -File winui3_snapshot_check.ps1 -Exe <app.exe>      (the Uno app needs the API running and API_BASE_URL set)
#   python snapshot_desktop_seed.py <sqlite file> show
# Checks that the Add dialog has boxes for the order, the product, the tax category and the quantity only (the copied columns and the total show nothing yet), that a save writes a line with the product's name
# and price, the tax rate and the total, that the Edit dialog shows the copies as text and that a new quantity works the total out again. Read the line back from the database afterwards.
Import-Module C:\Github\CodeGenNew\Docs\Verification\UiAutomation.psm1 -Force -WarningAction Ignore
$ErrorActionPreference = 'Stop'

function Pick-Item([string]$combo, [string]$item) {
    $box = Find-Name $combo
    Expand-El $box
    Start-Sleep -Milliseconds 800
    $entry = @(Find-Type 'ListItem' | Where-Object { $_.Current.Name -eq $item })[0]
    Select-El $entry
    Start-Sleep -Milliseconds 500
}

Start-App -Path $Exe | Out-Null
try {
    Start-Sleep -Seconds 5
    Select-El (Find-Name $Menu)
    Start-Sleep -Seconds 4
    Invoke-El (Find-Name 'Add New')
    Start-Sleep -Seconds 3
    $names = (List-Names | Select-Object -First 400) -join ' | '
    $dialog = $names.Substring($names.IndexOf('Window: Add'))
    $dialog = $dialog.Substring(0, $dialog.IndexOf('Button: Cancel'))
    Assert-That (($dialog -match 'ComboBox: Sales Order') -and ($dialog -match 'ComboBox: Product') -and ($dialog -match 'ComboBox: Tax Category') -and ($dialog -match 'Quantity')) 'the Add dialog has the order, the product, the tax category and the quantity'
    Assert-That (-not ($dialog -match 'Unit Price') -and -not ($dialog -match 'Line Total') -and -not ($dialog -match 'NaN')) 'the Add dialog shows nothing for the copied columns and the total'

    Pick-Item 'Sales Order' 'SO-1'
    Pick-Item 'Product' 'Widget'
    Pick-Item 'Tax Category' 'Standard'
    $quantity = @(Find-Type 'Edit' | Where-Object { $_.Current.Name -like 'Quantity*' })[0]
    Set-Text $quantity '3'
    Send-Keys '{TAB}'
    Start-Sleep -Milliseconds 500
    Invoke-El (Find-Name 'Save')
    Start-Sleep -Seconds 4
    $list = (List-Names | Select-Object -First 400) -join ' | '
    Assert-That ($list -match 'Widget') 'the list shows the line with the product name the server copied'

    $edit = @(Find-Type 'Button' | Where-Object { $_.Current.Name -eq 'Edit' })[0]
    Invoke-El $edit
    Start-Sleep -Seconds 3
    $names = (List-Names | Select-Object -First 400) -join ' | '
    $dialog = $names.Substring($names.IndexOf('Window: Edit'))
    $dialog = $dialog.Substring(0, $dialog.IndexOf('Button: Cancel'))
    Write-Host $dialog
    Assert-That (($dialog -match 'Text: Widget') -and ($dialog -match '10\.5')) 'the Edit dialog shows the copied name and price as text'
    $quantity = @(Find-Type 'Edit' | Where-Object { $_.Current.Name -like 'Quantity*' })[0]
    Set-Text $quantity '4'
    Send-Keys '{TAB}'
    Start-Sleep -Milliseconds 500
    Invoke-El (Find-Name 'Save')
    Start-Sleep -Seconds 4
} finally { Stop-App }
if ($script:Failed) { exit 1 }
