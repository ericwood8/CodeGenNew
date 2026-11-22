param([Parameter(Mandatory)][string]$Exe, [string]$Menu = 'Invoice', [string]$Python = 'python', [string]$Database = '')
# Clicks through the header dialog of DocumentTotals (the settings of Matrix/document_totals.settings) in a generated WinUI3 or Uno (Windows target) app over a scratch copy of Schemas/document_totals.sql with
# UI Automation (the Uno app goes through the API and needs it running with API_BASE_URL set):
#   one invoice (Acme, USD) with lines of 100 and 50, ItemsTotal 150, TaxExclusive 150, TaxInclusive 150, Payable 150
#   powershell -File winui3_totals_check.ps1 -Exe <app.exe> -Database <sqlite file>
# Checks that the totals of the dialog are text (not boxes) and the prepaid amount is a box, and that a save with prepaid 20 stores payable 130 (read back from the database when -Database is given).
Import-Module C:\Github\CodeGenNew\Docs\Verification\UiAutomation.psm1 -Force -WarningAction Ignore
$ErrorActionPreference = 'Stop'

function Open-Edit {
    $edit = @(Find-Type 'Button' | Where-Object { $_.Current.Name -eq 'Edit' })[0]
    Invoke-El $edit
    Start-Sleep -Seconds 3
}

function Dialog-Window { @(Find-Like 'Edit *' | Where-Object { $_.Current.ControlType.ProgrammaticName -eq 'ControlType.Window' })[0] }

function Dialog-Text {
    $names = (List-Names | Select-Object -First 600) -join ' | '
    $start = $names.IndexOf('Window: Edit')
    if ($start -lt 0) { return $names }
    $dialog = $names.Substring($start)
    $end = $dialog.IndexOf('Button: Cancel')
    if ($end -gt 0) { $dialog = $dialog.Substring(0, $end) }
    $dialog
}

function Stored {
    if (-not $Database) { return '' }
    (& $Python -c "import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); print(c.execute('select ItemsTotal,Prepaid,Rounding,Payable from Invoice').fetchall())" $Database) -join ' '
}

Start-App -Path $Exe | Out-Null
try {
    Start-Sleep -Seconds 5
    Select-El (Find-Name $Menu)
    Start-Sleep -Seconds 4
    Open-Edit
    $dialog = Dialog-Text
    Write-Host $dialog
    foreach ($label in 'Items Total', 'Allowance Total', 'Charge Total', 'Tax Exclusive', 'Tax Total', 'Tax Inclusive', 'Rounding', 'Payable') {
        Assert-That ($dialog -match [regex]::Escape($label)) "the dialog lists $label"
    }
    $window = Dialog-Window
    $boxes = @(Find-Type 'Edit' $window | ForEach-Object { $_.Current.Name })
    Write-Host ('boxes: ' + ($boxes -join ' | '))
    Assert-That (@($boxes | Where-Object { $_ -like 'Prepaid*' }).Count -ge 1) 'the prepaid amount is a box'
    foreach ($label in 'Items Total', 'Allowance Total', 'Charge Total', 'Tax Exclusive', 'Tax Total', 'Tax Inclusive', 'Rounding', 'Payable') {
        Assert-That (@($boxes | Where-Object { $_ -like "$label*" }).Count -eq 0) "$label is shown as text, not a box"
    }
    $prepaid = @(Find-Type 'Edit' $window | Where-Object { $_.Current.Name -like 'Prepaid*' })[0]
    $prepaid.SetFocus()
    Start-Sleep -Milliseconds 400
    Set-Text $prepaid '20'
    Send-Keys '{TAB}'
    Start-Sleep -Milliseconds 500
    (Find-Name 'Save').SetFocus()
    Start-Sleep -Milliseconds 500
    Invoke-El (Find-Name 'Save')
    Start-Sleep -Seconds 4
    if ($Database) {
        $row = Stored
        Write-Host $row
        Assert-That ($row -match '20(\.0+)?, 0(\.0+)?, 130(\.0+)?\)') "prepaid 20 is stored and payable is worked out as 130 ($row)"
    }
} finally { Stop-App }
if ($script:Failed) { exit 1 }
