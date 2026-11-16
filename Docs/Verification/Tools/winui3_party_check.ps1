param([Parameter(Mandatory)][string]$Exe, [string]$Menu = 'Party', [string]$IdentifierMenu = 'Party Identifier')
# Clicks through the list screen of a party table (PartyTables=Party:PartyIdentifier) and the edit dialog of an identifier in a generated WinUI3 or Uno (Windows target) app over a scratch copy of
# Schemas/parties.sql seeded by party_desktop_seed.py, with UI Automation:
#   powershell -File winui3_party_check.ps1 -Exe <app.exe>      (the Uno app needs the API running and API_BASE_URL set)
# Checks that the Find by identifier box lists the parties that hold a value whatever its scheme and its case, that a value nobody holds lists nothing, that Show all brings the rows back, and that the edit
# dialog of an identifier refuses a scheme and value pair another row holds and saves one that is free. Read the identifiers back from the database afterwards (party_desktop_seed.py show).
Import-Module C:\Github\CodeGenNew\Docs\Verification\UiAutomation.psm1 -Force -WarningAction Ignore
$ErrorActionPreference = 'Stop'

function Count-Buttons([string]$name) { @(Find-Type 'Button' | Where-Object { $_.Current.Name -eq $name }).Count }
function Screen-Text { (List-Names | Select-Object -First 400) -join ' | ' }
function Find-By([string]$value) {
    $box = Find-Name 'Find by identifier'
    $box.SetFocus()
    Start-Sleep -Milliseconds 500
    Set-Text $box $value
    (Find-Name 'Find').SetFocus()   # a text box writes its value back when it loses focus
    Invoke-El (Find-Name 'Find')
    Start-Sleep -Seconds 3
}
function Save-Scheme([string]$scheme) {
    # the list behind the dialog has a Scheme search box too: take the one that shares an ancestor with Save
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $scope = Find-Name 'Save'
    do { $scope = $walker.GetParent($scope); $box = Find-Name 'Scheme' $scope } until ($box)
    $box.SetFocus()
    Start-Sleep -Milliseconds 500
    Set-Text $box $scheme
    (Find-Name 'Save').SetFocus()
    Invoke-El (Find-Name 'Save')
    Start-Sleep -Seconds 3
}

Start-App -Path $Exe | Out-Null
try {
    Start-Sleep -Seconds 5
    Select-El (Find-Name $Menu)
    Start-Sleep -Seconds 4
    Assert-That ((Count-Buttons 'Edit') -eq 3) 'the list starts with the three parties'

    Find-By 'gb123'
    $text = Screen-Text
    Assert-That ((Count-Buttons 'Edit') -eq 2 -and $text.Contains('Text: Acme') -and $text.Contains('Text: Bolt')) 'a value two parties hold under different schemes lists both, whatever its case'
    Assert-That ($text.Contains('Page 1 of 1 (2 rows)')) 'the parties found are one page'

    Find-By ' DE999 '
    $text = Screen-Text
    Assert-That ((Count-Buttons 'Edit') -eq 1 -and $text.Contains('Text: Core')) 'a value one party holds lists that party; spaces around it do not count'

    Find-By 'nobody'
    Assert-That ((Count-Buttons 'Edit') -eq 0 -and (Screen-Text).Contains('Nothing found.')) 'a value nobody holds lists nothing'

    Invoke-El (Find-Name 'Show all')
    Start-Sleep -Seconds 3
    Assert-That ((Count-Buttons 'Edit') -eq 3) 'Show all brings the three parties back'
    Assert-That ([string]::IsNullOrEmpty((Get-Text (Find-Name 'Find by identifier')))) 'Show all empties the box'

    # the identifiers: Bolt holds DUNS GB123, and VAT GB123 is Acme's
    Select-El (Find-Name $IdentifierMenu)
    Start-Sleep -Seconds 4
    Assert-That ((Count-Buttons 'Edit') -eq 4) 'the identifier list has the four identifiers'
    $row = @(Find-Type 'ListItem' | Where-Object { (List-Names $_) -contains 'Text: Bolt' })[0]
    Invoke-El (@(Find-Type 'Button' $row | Where-Object { $_.Current.Name -eq 'Edit' })[0])
    Wait-Name 'Save' | Out-Null
    Start-Sleep -Seconds 1
    Save-Scheme ' vat '
    $text = Screen-Text
    Assert-That ($text.Contains('Another row already holds this identifier of this scheme.')) 'a scheme and value pair another row holds is refused in the dialog'
    if (Find-Name 'Cancel') {
        Save-Scheme 'LEI'
        Assert-That (-not (Find-Name 'Cancel')) 'a pair that is free is saved and the dialog closes'
    }
    if (Find-Name 'Cancel') { Invoke-El (Find-Name 'Cancel') }
} finally { Stop-App }
if ($script:Failed) { exit 1 }
