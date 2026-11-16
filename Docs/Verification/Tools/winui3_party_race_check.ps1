param([Parameter(Mandatory)][string]$Exe, [Parameter(Mandatory)][string]$DbFile, [string]$Menu = 'Party', [string]$IdentifierMenu = 'Party Identifier')
# Plays the race of the WinUI3 dialogs of a party table (PartyTables=Party:PartyIdentifier, EditableChildren=PartyIdentifier) in a generated app over a scratch copy of Schemas/parties.sql seeded by
# party_desktop_seed.py: another writer takes the pair between the dialog's check and its save, so the unique index on the pair refuses the save.
#   python party_desktop_seed.py <sqlite file> seed
#   powershell -File winui3_party_race_check.ps1 -Exe <app.exe> -DbFile <the same sqlite file>
# party_rival_writer.py holds the write lock of the file while the dialog checks, then commits the pair, so the dialog's save finds it. The party dialog (identifier rows inside it) and the dialog of the identifier table each show the rule's sentence, keep the dialog open and keep none of the refused values. Read the rows back with
# party_desktop_seed.py show: the rival rows are the only ones with a RACE value.
Import-Module C:\Github\CodeGenNew\Docs\Verification\UiAutomation.psm1 -Force -WarningAction Ignore
$ErrorActionPreference = 'Stop'

function Screen-Text { (List-Names | Select-Object -First 400) -join ' | ' }
function Save-Box([string]$box, [string]$text, [string]$scheme) {
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $scope = Find-Name 'Save'
    do { $scope = $walker.GetParent($scope); $el = Find-Name $box $scope } until ($el)
    $el.SetFocus()
    Start-Sleep -Milliseconds 500
    Set-Text $el $text
    (Find-Name 'Save').SetFocus()   # a text box writes its value back when it loses focus
    $rival = Start-Process python -ArgumentList @((Join-Path $PSScriptRoot 'party_rival_writer.py'), $DbFile, $scheme, $text, '6') -PassThru -WindowStyle Hidden
    Start-Sleep -Seconds 1.5
    Invoke-El (Find-Name 'Save')
    $rival.WaitForExit()
    Start-Sleep -Seconds 4
}
$taken = 'Another row already holds this identifier of this scheme.'

Start-App -Path $Exe | Out-Null
try {
    Start-Sleep -Seconds 5
    Select-El (Find-Name $Menu)
    Start-Sleep -Seconds 4
    $row = @(Find-Type 'ListItem' | Where-Object { (List-Names $_) -contains 'Text: Bolt' })[0]
    Invoke-El (@(Find-Type 'Button' $row | Where-Object { $_.Current.Name -eq 'Edit' })[0])
    Wait-Name 'Save' | Out-Null
    Start-Sleep -Seconds 2
    Save-Box 'Value' 'RACE1' 'DUNS'
    Assert-That ((Screen-Text).Contains($taken)) 'a pair taken between the check and the save of an identifier row in the party dialog is the rule sentence'
    Assert-That ([bool](Find-Name 'Cancel')) 'the party dialog stays open'
    if (Find-Name 'Cancel') { Invoke-El (Find-Name 'Cancel') }
    Start-Sleep -Seconds 2

    Select-El (Find-Name $IdentifierMenu)
    Start-Sleep -Seconds 4
    $row = @(Find-Type 'ListItem' | Where-Object { (List-Names $_) -contains 'Text: DE999' })[0]
    Invoke-El (@(Find-Type 'Button' $row | Where-Object { $_.Current.Name -eq 'Edit' })[0])
    Wait-Name 'Save' | Out-Null
    Start-Sleep -Seconds 2
    Save-Box 'Value' 'RACE2' 'VAT'
    Assert-That ((Screen-Text).Contains($taken)) 'a pair taken between the check and the save in the dialog of the identifier table is the rule sentence'
    Assert-That ([bool](Find-Name 'Cancel')) 'the identifier dialog stays open'
    if (Find-Name 'Cancel') { Invoke-El (Find-Name 'Cancel') }
} finally { Stop-App }
if ($script:Failed) { exit 1 }
