param([Parameter(Mandatory)][string]$Exe, [string]$Menu = 'Pay Transaction')
# Clicks through the list screen of a ledger table (LedgerTables=PayTransaction) in a generated WinUI3 or Uno (Windows target) app over a scratch copy of Schemas/payments.sql, with UI Automation:
#   python ledger_desktop_seed.py <scratch sqlite file>
#   powershell -File winui3_ledger_check.ps1 -Exe <app.exe>      (the Uno app needs the API running and API_BASE_URL set)
# Checks that the screen is read only (no Add New and no Edit or Delete button) and that the Show paid button answers what a document has been paid, counting only the succeeded rows (captured less refunded),
# says when a document is paid in full, and says when there is no such document or the box is empty.
Import-Module C:\Github\CodeGenNew\Docs\Verification\UiAutomation.psm1 -Force -WarningAction Ignore
$ErrorActionPreference = 'Stop'

function Ask-Paid([string]$document) {
    $box = Find-Name 'Paid document'
    $box.SetFocus()
    Start-Sleep -Milliseconds 400
    Set-Text $box $document
    (Find-Name 'Show paid').SetFocus()   # a text box writes its value back when it loses focus
    Invoke-El (Find-Name 'Show paid')
    Start-Sleep -Seconds 3
    $all = (List-Names | Select-Object -First 400) -join ' | '
    if ($all -match 'Button: Show paid \| Text: ([^|]*)') { $Matches[1].Trim() } else { $all }
}

Start-App -Path $Exe | Out-Null
try {
    Start-Sleep -Seconds 5
    Select-El (Find-Name $Menu)
    Start-Sleep -Seconds 4

    $page = (List-Names | Select-Object -First 400) -join ' | '
    Assert-That (-not $page.Contains('Add New')) "a ledger has no Add New button"
    $answer = Ask-Paid ' 1 '
    Assert-That ($answer -match 'Paid 150(\.0+)? of 300(\.0+)?, 150(\.0+)? left') "document 1 is paid 200 less a refund of 50, and the pending capture is not counted ($answer)"
    $answer = Ask-Paid '2'
    Assert-That ($answer -match 'Paid in full: 100(\.0+)? of 100(\.0+)?') "document 2 is paid in full ($answer)"
    $answer = Ask-Paid '99'
    Assert-That ($answer.Contains('There is no document with that number.')) "a document that does not exist has none ($answer)"
    $answer = Ask-Paid ' '
    Assert-That ($answer.Contains('Give the document number.')) "an empty box asks for the document ($answer)"
} finally { Stop-App }
if ($script:Failed) { exit 1 }
