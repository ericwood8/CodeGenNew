param([Parameter(Mandatory)][string]$Exe, [string]$Menu = 'Invoice Allowance Charge', [string]$Python = 'python', [string]$Database = '')
# Clicks through the allowances and charges (AllowanceCharges=InvoiceAllowanceCharge:IsCharge:BaseAmount:Percentage:Amount:InvoiceId) in a generated WinUI3 or Uno (Windows target) app over a scratch copy of
# Schemas/allowance_charges_document.sql with UI Automation (the Uno app goes through the API and needs it running with API_BASE_URL set):
#   python allowance_desktop_seed.py <sqlite file> seed
#   powershell -File winui3_allowance_check.ps1 -Exe <app.exe> -Database <sqlite file>
# Checks that the list's totals box adds up the document's rows, that the invoice's dialog shows the same line under the grid, and that a row saved with a base, a percentage and a wrong amount stores the
# amount worked out from the two (read back from the database when -Database is given).
Import-Module C:\Github\CodeGenNew\Docs\Verification\UiAutomation.psm1 -Force -WarningAction Ignore
$ErrorActionPreference = 'Stop'
$seed = Join-Path $PSScriptRoot 'allowance_desktop_seed.py'

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

function Set-Field([string]$label, [string]$value) {
    $dialog = @(Find-Like 'Edit *' | Where-Object { $_.Current.ControlType.ProgrammaticName -eq 'ControlType.Window' })[0]
    $box = @(Find-Type 'Edit' $dialog | Where-Object { $_.Current.Name -like "$label*" })[0]
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

    $box = Find-Name 'Allowance document'
    $box.SetFocus()
    Start-Sleep -Milliseconds 400
    Set-Text $box '1'
    (Find-Name 'Show totals').SetFocus()   # a text box writes its value back when it loses focus
    Invoke-El (Find-Name 'Show totals')
    Start-Sleep -Seconds 3
    $names = (List-Names | Select-Object -First 400) -join ' | '
    Assert-That ($names -match 'Allowances 25, charges 60, net 35 \(3 rows\)') "the totals box adds up the document's rows ($names)"

    Select-El (Find-Name 'Invoice')
    Start-Sleep -Seconds 4
    Open-Edit
    $dialog = Dialog-Text
    Write-Host $dialog
    Assert-That ($dialog -match 'Allowances 25, charges 60, net 35 \(3 rows\)') "the invoice's dialog shows the same line under the grid ($dialog)"
    Invoke-El (Find-Name 'Cancel')
    Start-Sleep -Seconds 2

    Select-El (Find-Name $Menu)
    Start-Sleep -Seconds 4
    Open-Edit
    Set-Field 'Base Amount' '300'
    Set-Field 'Percentage' '5'
    Set-Field 'Amount' '1'
    (Find-Name 'Save').SetFocus()
    Start-Sleep -Milliseconds 500
    Invoke-El (Find-Name 'Save')
    Start-Sleep -Seconds 4
    if ($Database) {
        $rows = (& $Python $seed $Database show) -join ' '
        Write-Host $rows
        Assert-That ($rows -match '300(\.0+)?, 5(\.0+)?, 15(\.0+)?\)') "the amount typed as 1 is stored as 15, worked out from the base and the percentage ($rows)"
    }
} finally { Stop-App }
if ($script:Failed) { exit 1 }
