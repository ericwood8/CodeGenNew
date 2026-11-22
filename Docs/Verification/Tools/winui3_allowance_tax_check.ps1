param([Parameter(Mandatory)][string]$Exe, [string]$Menu = 'Invoice Allowance Charge', [string]$Python = 'python', [string]$Database = '', [switch]$NoRefusal)
# Clicks through the tax columns of an allowance row (DocumentTotals with AllowanceTax=TaxAmount and the allowance table listed in TaxLines) in a generated WinUI3 or Uno (Windows target) app over a scratch copy
# of Schemas/document_totals.sql with UI Automation (the Uno app goes through the API and needs it running with API_BASE_URL set; give no -Database for it):
#   python allowance_tax_desktop_seed.py <sqlite file> seed
#   powershell -File winui3_allowance_tax_check.ps1 -Exe <app.exe> -Database <sqlite file>
# Checks that the Edit dialog shows the stored rate and tax as text with no box, that a new amount keeps the stored rate and works the tax out again, that the invoice's totals follow, and that a category
# outside its validity is refused in words and keeps the dialog open.
Import-Module C:\Github\CodeGenNew\Docs\Verification\UiAutomation.psm1 -Force -WarningAction Ignore
$ErrorActionPreference = 'Stop'
$seed = Join-Path $PSScriptRoot 'allowance_tax_desktop_seed.py'

function Pick-Item([string]$combo, [string]$item) {
    # the combo inside the dialog: the menu has an entry of the same name
    $dialog = @(Find-Like 'Edit *' | Where-Object { $_.Current.ControlType.ProgrammaticName -eq 'ControlType.Window' })[0]
    $box = @(Find-Type 'ComboBox' $dialog | Where-Object { $_.Current.Name -like "$combo*" })[0]
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

function Set-Amount([string]$value) {
    $dialog = @(Find-Like 'Edit *' | Where-Object { $_.Current.ControlType.ProgrammaticName -eq 'ControlType.Window' })[0]
    $box = @(Find-Type 'Edit' $dialog | Where-Object { $_.Current.Name -like 'Amount*' })[0]
    $box.SetFocus()
    Start-Sleep -Milliseconds 400
    Set-Text $box $value
    Send-Keys '{TAB}'
    Start-Sleep -Milliseconds 500
}

function Stored { if ($Database) { (& $Python $seed $Database show) -join ' ' } else { '' } }

Start-App -Path $Exe | Out-Null
try {
    Start-Sleep -Seconds 5
    Select-El (Find-Name $Menu)
    Start-Sleep -Seconds 4
    Open-Edit
    $dialog = Dialog-Text
    Write-Host $dialog
    Assert-That (($dialog -match 'Text: 99%') -and ($dialog -match 'Text: 1 ')) 'the Edit dialog shows the stored rate (as a percentage) and tax as text'
    Assert-That (-not ($dialog -match 'Edit: Tax Rate') -and -not ($dialog -match 'Edit: Tax Amount')) 'the rate and the tax have no box'

    Set-Amount '40'
    Save-Dialog
    Open-Edit
    $dialog = Dialog-Text
    Write-Host $dialog
    Assert-That (($dialog -match 'Text: 99%') -and ($dialog -match 'Text: 39\.6')) 'a new amount keeps the rate the row was written with and works the tax out again'
    if ($Database) {
        $rows = Stored
        Write-Host $rows
        Assert-That ($rows -match '40(\.0+)?, 0\.99(0+)?, 39\.6') "the amount, the kept rate and the tax are stored ($rows)"
        Assert-That ($rows -match '\(40(\.0+)?, -39\.6(0)?, -79\.6(0)?\)') "the invoice's totals follow (allowances 40, tax -39.6, payable -79.6) ($rows)"
    }

    if (-not $NoRefusal) {   # UI Automation cannot pick an entry of the Uno drop-down, so the Uno app is run with -NoRefusal
        Pick-Item 'Tax Category' 'OLD'
        Save-Dialog
        $dialog = Dialog-Text
        Assert-That (($dialog -match 'Window: Edit') -and ($dialog -match 'not in force')) "a category outside its validity is refused in words and the dialog stays open ($dialog)"
        Invoke-El (Find-Name 'Cancel')
    }
} finally { Stop-App }
if ($script:Failed) { exit 1 }
