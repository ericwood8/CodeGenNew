param([Parameter(Mandatory)][string]$Exe, [string]$Menu = 'Exchange Rate')
# Clicks through the list screen of a rate table (RateTables=ExchangeRate) in a generated WinUI3 or Uno (Windows target) app over a scratch copy of Schemas/rates.sql that holds two USD to EUR rates
# (1.5 from 2024-01-01 and 1.7 from 2024-03-01), with UI Automation:
#   python rate_desktop_seed.py <scratch sqlite file>
#   powershell -File winui3_rate_check.ps1 -Exe <app.exe>      (the Uno app needs the API running and API_BASE_URL set)
# Checks that the Find rate button shows the rate in force on a day (trimmed currencies, any case), that a day before the first rate and a pair with no rates say there is none, and that a day that is
# not a date is refused.
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

Start-App -Path $Exe | Out-Null
try {
    Start-Sleep -Seconds 5
    Select-El (Find-Name $Menu)
    Start-Sleep -Seconds 4

    $answer = Ask-Rate ' usd ' 'eur' '2024-02-15'
    Assert-That ($answer.Contains('USD to EUR: 1.5')) "a day between two rates gets the earlier one ($answer)"
    $answer = Ask-Rate 'USD' 'EUR' '2024-03-01'
    Assert-That ($answer.Contains('USD to EUR: 1.7')) "the day a rate starts gets that rate ($answer)"
    $answer = Ask-Rate 'USD' 'EUR' '2023-12-31'
    Assert-That ($answer.Contains('No rate for that pair')) "a day before the first rate has none ($answer)"
    $answer = Ask-Rate 'USD' 'GBP' '2024-03-01'
    Assert-That ($answer.Contains('No rate for that pair')) "a pair with no rates has none ($answer)"
    $answer = Ask-Rate 'USD' 'EUR' 'tomorrow'
    Assert-That ($answer.Contains('yyyy-mm-dd')) "a day that is not a date is refused ($answer)"
} finally { Stop-App }
if ($script:Failed) { exit 1 }
