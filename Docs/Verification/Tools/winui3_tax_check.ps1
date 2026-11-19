param([Parameter(Mandatory)][string]$Exe, [Parameter(Mandatory)][string]$Database, [string]$Menu = 'Invoice Line', [string]$Python = 'python')
# Clicks through the tax lines (TaxLines=InvoiceLine:TaxCategoryId:TaxRate:LineTotal:TaxAmount:TaxCategory:Rate:ValidFrom:ValidTo:LineDate:InvoiceId:Code) in a generated WinUI3 app over a scratch copy of
# Schemas/tax_lines_document.sql with UI Automation (the Uno app goes through the API and needs it running with API_BASE_URL set):
#   python tax_desktop_seed.py <sqlite file> seed
#   powershell -File winui3_tax_check.ps1 -Exe <app.exe> -Database <sqlite file>
# Checks that the invoice's dialog shows the tax of its lines under the line grid, that the list's tax box shows the document's tax by category and the rate the line was written with, that the Edit dialog shows the rate and the tax as text with no box, that a new total keeps the
# rate and works the tax out again, that another category brings its rate, and that a category outside its validity is refused in words and keeps the dialog open.
Import-Module C:\Github\CodeGenNew\Docs\Verification\UiAutomation.psm1 -Force -WarningAction Ignore
$ErrorActionPreference = 'Stop'
$seed = Join-Path $PSScriptRoot 'tax_desktop_seed.py'

function Pick-Item([string]$combo, [string]$item) {
    $box = Find-Name $combo
    Expand-El $box
    Start-Sleep -Milliseconds 800
    $entry = @(Find-Type 'ListItem' | Where-Object { $_.Current.Name -eq $item })[0]
    Select-El $entry
    Start-Sleep -Milliseconds 500
}

function Open-Edit {
    $edit = @(Find-Type 'Button' | Where-Object { $_.Current.Name -eq 'Edit' })[0]
    Invoke-El $edit
    Start-Sleep -Seconds 3
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

function Save-Dialog {
    (Find-Name 'Save').SetFocus()
    Start-Sleep -Milliseconds 500
    Invoke-El (Find-Name 'Save')
    Start-Sleep -Seconds 4
}

function Set-Total([string]$value) {
    $dialog = @(Find-Like 'Edit *' | Where-Object { $_.Current.ControlType.ProgrammaticName -eq 'ControlType.Window' })[0]
    $box = @(Find-Type 'Edit' $dialog | Where-Object { $_.Current.Name -like 'Line Total*' })[0]
    $box.SetFocus()
    Start-Sleep -Milliseconds 400
    Set-Text $box $value
    Send-Keys '{TAB}'
    Start-Sleep -Milliseconds 500
}

Start-App -Path $Exe | Out-Null
try {
    Start-Sleep -Seconds 5
    Select-El (Find-Name $Menu)
    Start-Sleep -Seconds 4

    $box = Find-Name 'Tax document'
    $box.SetFocus()
    Start-Sleep -Milliseconds 400
    Set-Text $box '1'
    (Find-Name 'Show tax').SetFocus()   # a text box writes its value back when it loses focus
    Invoke-El (Find-Name 'Show tax')
    Start-Sleep -Seconds 3
    $names = (List-Names | Select-Object -First 400) -join ' | '
    Assert-That ($names -match 'VAT20: 99% on 100 = 1') "the tax box shows the document's tax by category and the rate the line was written with ($names)"

    Select-El (Find-Name 'Invoice')
    Start-Sleep -Seconds 4
    Open-Edit
    $dialog = Dialog-Text
    Write-Host $dialog
    Assert-That ($dialog -match 'VAT20: 99% on 100 = 1') "the invoice's dialog shows the tax of its lines by category and rate under the line grid ($dialog)"
    Invoke-El (Find-Name 'Cancel')
    Start-Sleep -Seconds 2
    Select-El (Find-Name $Menu)
    Start-Sleep -Seconds 4
    Open-Edit
    $dialog = Dialog-Text
    Write-Host $dialog
    Assert-That (($dialog -match 'Text: 0\.99') -and ($dialog -match 'Text: 1 ')) 'the Edit dialog shows the stored rate and tax as text'
    Assert-That (-not ($dialog -match 'Edit: Tax Rate') -and -not ($dialog -match 'Edit: Tax Amount')) 'the rate and the tax have no box'

    Set-Total '200'
    Save-Dialog
    Open-Edit
    $dialog = Dialog-Text
    Write-Host $dialog
    Assert-That (($dialog -match 'Text: 0\.99') -and ($dialog -match 'Text: 198')) 'a new total keeps the rate the line was written with and works the tax out again'

    Pick-Item 'Tax Category' 'VAT5'
    Save-Dialog
    Open-Edit
    $dialog = Dialog-Text
    Write-Host $dialog
    Assert-That (($dialog -match 'Text: 0\.05') -and ($dialog -match 'Text: 10 ')) 'another category brings its rate and the tax follows'
    Invoke-El (Find-Name 'Cancel')
    Start-Sleep -Seconds 2

    & $Python $seed $Database expire | Out-Null
    Open-Edit
    Pick-Item 'Tax Category' 'VAT20'
    Save-Dialog
    $dialog = Dialog-Text
    Assert-That (($dialog -match 'Window: Edit') -and ($dialog -match 'not in force')) "a category outside its validity is refused in words and the dialog stays open ($dialog)"
    Invoke-El (Find-Name 'Cancel')
} finally { Stop-App }
if ($script:Failed) { exit 1 }
