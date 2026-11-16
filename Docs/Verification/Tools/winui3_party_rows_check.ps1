param([Parameter(Mandatory)][string]$Exe, [string]$Menu = 'Party')
# Clicks through the edit dialog of a party whose identifiers are rows inside it (PartyTables=Party:PartyIdentifier, EditableChildren=PartyIdentifier) in a generated WinUI3 or Uno (Windows target) app over a
# scratch copy of Schemas/parties.sql seeded by party_desktop_seed.py, with UI Automation:
#   powershell -File winui3_party_rows_check.ps1 -Exe <app.exe>      (the Uno app needs the API running and API_BASE_URL set)
# Bolt holds DUNS GB123 and VAT GB123 is Acme's: the dialog of Bolt refuses the row changed to " vat " and saves it as LEI. Read the identifiers back from the database afterwards (party_desktop_seed.py show).
Import-Module C:\Github\CodeGenNew\Docs\Verification\UiAutomation.psm1 -Force -WarningAction Ignore
$ErrorActionPreference = 'Stop'

function Screen-Text { (List-Names | Select-Object -First 400) -join ' | ' }
function Save-Scheme([string]$scheme) {
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $scope = Find-Name 'Save'
    do { $scope = $walker.GetParent($scope); $box = Find-Name 'Scheme' $scope } until ($box)
    $box.SetFocus()
    Start-Sleep -Milliseconds 500
    Set-Text $box $scheme
    (Find-Name 'Save').SetFocus()   # a text box writes its value back when it loses focus
    Invoke-El (Find-Name 'Save')
    Start-Sleep -Seconds 3
}

Start-App -Path $Exe | Out-Null
try {
    Start-Sleep -Seconds 5
    Select-El (Find-Name $Menu)
    Start-Sleep -Seconds 4
    $row = @(Find-Type 'ListItem' | Where-Object { (List-Names $_) -contains 'Text: Bolt' })[0]
    Invoke-El (@(Find-Type 'Button' $row | Where-Object { $_.Current.Name -eq 'Edit' })[0])
    Wait-Name 'Save' | Out-Null
    Start-Sleep -Seconds 2
    Save-Scheme ' vat '
    Assert-That ((Screen-Text).Contains('Another row already holds this identifier of this scheme.')) 'an identifier row changed to a pair another party holds is refused in the party dialog'
    if (Find-Name 'Cancel') {
        Save-Scheme ' LEI '
        Assert-That (-not (Find-Name 'Cancel')) 'a pair that is free is saved with the party and the dialog closes'
    }
    if (Find-Name 'Cancel') { Invoke-El (Find-Name 'Cancel') }
} finally { Stop-App }
if ($script:Failed) { exit 1 }
