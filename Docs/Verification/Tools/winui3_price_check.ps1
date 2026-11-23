param([Parameter(Mandatory)][string]$Exe, [string]$Menu = 'Price List Item')
# Clicks through the list screen and the Edit dialog of a price table (PriceListTables=PriceListItem:PriceListId:ItemId:MinQuantity:Price:PriceList:CurrencyCode:ValidFrom:ValidTo:CustomerGroup) in a generated
# WinUI3 or Uno (Windows target) app over a scratch copy of Schemas/price_lists.sql, with UI Automation:
#   python price_desktop_seed.py <scratch sqlite file>
#   powershell -File winui3_price_check.ps1 -Exe <app.exe>      (the Uno app needs the API running and API_BASE_URL set)
# Checks that the Find price button shows the price in force (the break below the quantity, any case of the currency, a group's list first), that a day with no list and a bad day say so, and (WinUI3, whose
# dialog writes through its own context) that the Edit dialog refuses a minimum quantity of zero, a negative price and a break the list already holds, in words, and keeps the dialog open.
Import-Module C:\Github\CodeGenNew\Docs\Verification\UiAutomation.psm1 -Force -WarningAction Ignore
$ErrorActionPreference = 'Stop'

function Ask-Price([string]$item, [string]$quantity, [string]$currency, [string]$day, [string]$group) {
    foreach ($pair in @(@('Price item', $item), @('Price quantity', $quantity), @('Price currency', $currency), @('Price on', $day), @('Price customer group', $group))) {
        $box = Find-Name $pair[0]
        $box.SetFocus()
        Start-Sleep -Milliseconds 400
        Set-Text $box $pair[1]
    }
    (Find-Name 'Find price').SetFocus()   # a text box writes its value back when it loses focus
    Invoke-El (Find-Name 'Find price')
    Start-Sleep -Seconds 3
    (List-Names | Select-Object -First 400) -join ' | '
}

function Dialog-Text {
    $names = (List-Names | Select-Object -First 500) -join ' | '
    $start = $names.IndexOf('Window: Edit')
    if ($start -lt 0) { return $names }
    $dialog = $names.Substring($start)
    $end = $dialog.IndexOf('Button: Cancel')
    if ($end -gt 0) { $dialog = $dialog.Substring(0, $end) }
    $dialog
}

function Save-Row([string]$min, [string]$price) {
    $dialog = @(Find-Like 'Edit *' | Where-Object { $_.Current.ControlType.ProgrammaticName -eq 'ControlType.Window' })[0]
    foreach ($pair in @(@('Min Quantity*', $min), @('Price*', $price))) {
        $box = @(Find-Type 'Edit' $dialog | Where-Object { $_.Current.Name -like $pair[0] })[0]
        $box.SetFocus()
        Start-Sleep -Milliseconds 400
        Set-Text $box $pair[1]
    }
    (Find-Name 'Save').SetFocus()
    Start-Sleep -Milliseconds 500
    Invoke-El (Find-Name 'Save')
    Start-Sleep -Seconds 4
}

Start-App -Path $Exe | Out-Null
try {
    Start-Sleep -Seconds 5
    Select-El (Find-Name $Menu)
    Start-Sleep -Seconds 4

    $answer = Ask-Price ' 1 ' '12' 'usd' '2024-06-01' ' '
    Assert-That ($answer.Contains('Price: 9 (from 10, list 1)')) "a quantity of 12 gets the break at 10 ($answer)"
    $answer = Ask-Price '1' '1' 'USD' '2024-06-01' 'gold'
    Assert-That ($answer.Contains('Price: 9.5 (from 1, list 2)')) "a group's list comes first ($answer)"
    $answer = Ask-Price '1' '100' 'USD' '2024-06-01' ' '
    Assert-That ($answer.Contains('Price: 8 (from 100, list 1)')) "a quantity of 100 gets the break at 100 ($answer)"
    $answer = Ask-Price '1' '1' 'USD' '2020-01-01' ' '
    Assert-That ($answer.Contains('No price for that item')) "a day before any list has none ($answer)"
    $answer = Ask-Price '1' '1' 'USD' 'tomorrow' ' '
    Assert-That ($answer.Contains('yyyy-mm-dd')) "a day that is not a date is refused ($answer)"

    if (-not $env:API_BASE_URL) {
        $edit = @(Find-Type 'Button' | Where-Object { $_.Current.Name -eq 'Edit' })[0]
        Invoke-El $edit
        Start-Sleep -Seconds 3
        Write-Host (Dialog-Text)
        Save-Row '0' '5'
        $dialog = Dialog-Text
        Assert-That (($dialog -match 'Window: Edit') -and ($dialog -match 'minimum quantity must be greater than zero')) "a minimum quantity of zero is refused in words and the dialog stays open ($dialog)"
        Save-Row '1' '-1'
        $dialog = Dialog-Text
        Assert-That (($dialog -match 'Window: Edit') -and ($dialog -match 'price cannot be negative')) "a negative price is refused ($dialog)"
        Save-Row '10' '5'
        $dialog = Dialog-Text
        Assert-That (($dialog -match 'Window: Edit') -and ($dialog -match 'already holds a price')) "a break the list already holds is refused ($dialog)"
        Invoke-El (Find-Name 'Cancel')
    }
} finally { Stop-App }
if ($script:Failed) { exit 1 }
