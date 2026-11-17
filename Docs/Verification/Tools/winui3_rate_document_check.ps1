param([Parameter(Mandatory)][string]$Exe, [string]$Menu = 'Sales Invoice', [string]$RateMenu = 'Exchange Rate')
# Clicks through the rate lookup and an invoice's Edit dialog (RateTables=ExchangeRate, RateDocuments=SalesInvoice:CurrencyCode:FxRate:Total:BaseTotal:InvoiceDate, BaseCurrency=USD, RateInverse=true)
# in a generated WinUI3 or Uno (Windows target) app over a scratch copy of Schemas/rate_documents.sql with UI Automation:
#   python rate_document_desktop_seed.py <sqlite file> seed
#   powershell -File winui3_rate_document_check.ps1 -Exe <app.exe>      (the Uno app needs the API running and API_BASE_URL set)
#   python rate_document_desktop_seed.py <sqlite file> show
# Checks that the lookup says when a rate is worked out from the opposite pair, that the Edit dialog shows the invoice's rate and converted total as text, that a new currency takes the rate in force
# and works the total out again, that a currency with no rate is refused in words and keeps the dialog open, and that the base currency has rate 1.
Import-Module C:\Github\CodeGenNew\Docs\Verification\UiAutomation.psm1 -Force -WarningAction Ignore
$ErrorActionPreference = 'Stop'

function Ask-Rate([string]$from, [string]$to, [string]$day) {
    foreach ($pair in @(@('From currency', $from), @('To currency', $to), @('Rate on', $day))) {
        $box = Find-Name $pair[0]
        $box.SetFocus()
        Start-Sleep -Milliseconds 400
        Set-Text $box $pair[1]
    }
    (Find-Name 'Find rate').SetFocus()   # a text box writes its value back when it loses focus
    Invoke-El (Find-Name 'Find rate')
    Start-Sleep -Seconds 3
    (List-Names | Select-Object -First 400) -join ' | '
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

function Save-Currency([string]$code) {
    $dialog = @(Find-Like 'Edit *' | Where-Object { $_.Current.ControlType.ProgrammaticName -eq 'ControlType.Window' })[0]
    $box = @(Find-Type 'Edit' $dialog | Where-Object { $_.Current.Name -like 'Currency Code*' })[0]
    $box.SetFocus()
    Start-Sleep -Milliseconds 400
    Set-Text $box $code
    (Find-Name 'Save').SetFocus()
    Start-Sleep -Milliseconds 500
    Invoke-El (Find-Name 'Save')
    Start-Sleep -Seconds 4
}

Start-App -Path $Exe | Out-Null
try {
    Start-Sleep -Seconds 5
    Select-El (Find-Name $RateMenu)
    Start-Sleep -Seconds 4
    $answer = Ask-Rate ' eur ' 'usd' '2024-02-15'
    Assert-That (($answer -match 'EUR to USD: 1\.1') -and -not ($answer -match 'one divided')) "the pair's own rate is not said to be worked out ($answer)"
    $answer = Ask-Rate 'EUR' 'USD' '2024-04-01'
    Assert-That (($answer -match 'EUR to USD: 2(\.0+)? ') -and ($answer -match 'one divided by the rate of the opposite pair')) "a newer opposite rate is answered as one divided by it, and says so ($answer)"

    Select-El (Find-Name $Menu)
    Start-Sleep -Seconds 4
    Open-Edit
    $dialog = Dialog-Text
    Write-Host $dialog
    Assert-That (($dialog -match 'Text: 99') -and ($dialog -match 'Text: 1 ') ) 'the Edit dialog shows the stored rate and converted total as text'
    Assert-That (-not ($dialog -match 'Edit: Fx Rate') -and -not ($dialog -match 'Edit: Base Total')) 'the rate and the converted total have no box'

    Save-Currency 'xyz'
    $dialog = Dialog-Text
    Assert-That (($dialog -match 'Window: Edit') -and ($dialog -match 'No exchange rate')) "a currency with no rate is refused in words and the dialog stays open ($dialog)"

    Save-Currency 'gbp'
    Open-Edit
    $dialog = Dialog-Text
    Write-Host $dialog
    Assert-That (($dialog -match 'Text: 1\.3') -and ($dialog -match 'Text: 130(\.0+)?\b')) 'a new currency takes the rate in force and works the total out again'
    Invoke-El (Find-Name 'Cancel')
    Start-Sleep -Seconds 2

    Open-Edit
    Save-Currency 'usd'
    Open-Edit
    $dialog = Dialog-Text
    Write-Host $dialog
    Assert-That (($dialog -match 'Text: 1 ') -and ($dialog -match 'Text: 100(\.0+)?\b')) 'the base currency has rate 1'
    Invoke-El (Find-Name 'Cancel')
} finally { Stop-App }
if ($script:Failed) { exit 1 }
