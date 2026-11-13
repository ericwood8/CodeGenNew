param([Parameter(Mandatory)][string]$Exe, [string]$Menu = 'Customer Address')
# Clicks through the list screen and the edit dialog of a default-row table (DefaultRowTables=CustomerAddress:IsDefault:CustomerId+AddressType) in a generated WinUI3 or Uno (Windows target) app over a
# scratch copy of Schemas/default_rows.sql with one customer and three addresses (a default billing address, a second billing address and a default shipping address), with UI Automation:
#   powershell -File winui3_default_check.ps1 -Exe <app.exe>      (the Uno app needs the API running and API_BASE_URL set)
# Checks that only the rows that are not the default of their group have a Make default button, that a click moves the mark (the button changes rows), that the edit dialog of a row that is not
# the default has the button and shows the flag as text without a check box, and that a click there makes the row the default. Read the rows back from the database afterwards (default_desktop_check.py does it).
Import-Module C:\Github\CodeGenNew\Docs\Verification\UiAutomation.psm1 -Force -WarningAction Ignore
$ErrorActionPreference = 'Stop'

function Count-Buttons([string]$name, $scope = $null) { @(Find-Type 'Button' $scope | Where-Object { $_.Current.Name -eq $name }).Count }
function First-Button([string]$name, $scope = $null) { @(Find-Type 'Button' $scope | Where-Object { $_.Current.Name -eq $name })[0] }

Start-App -Path $Exe | Out-Null
try {
    Start-Sleep -Seconds 5
    Select-El (Find-Name $Menu)
    Start-Sleep -Seconds 4
    Assert-That ((Count-Buttons 'Make default') -eq 1) 'only the row that is not the default of its group has a Make default button'

    Invoke-El (First-Button 'Make default')
    Start-Sleep -Seconds 3
    Assert-That ((Count-Buttons 'Make default') -eq 1) 'after the click the row that lost the mark has the button and the new default has none'

    # the edit dialog of the row that has the button
    $edits = @(Find-Type 'Button' | Where-Object { $_.Current.Name -eq 'Edit' })
    $found = $false
    foreach ($edit in $edits) {
        Invoke-El $edit
        Start-Sleep -Seconds 3
        $names = (List-Names | Select-Object -First 400) -join ' | '
        $at = $names.IndexOf('Window: Edit')
        if ($at -ge 0) {
            $dialog = $names.Substring($at)
            if ($dialog.Contains('Button: Cancel')) { $dialog = $dialog.Substring(0, $dialog.IndexOf('Button: Cancel')) }
            if ($dialog -match 'Button: Make default') {
                $found = $true
                Write-Host $dialog
                Assert-That (-not ($dialog -match 'CheckBox')) 'the edit dialog shows the flag as text, not a check box'
                Assert-That ($dialog -match 'Text: Default\? \| Text: No') 'the flag of a row that is not the default reads No'
                Invoke-El (First-Button 'Make default' (Get-AppWindow))
                Start-Sleep -Seconds 3
                $after = (List-Names | Select-Object -First 400) -join ' | '
                $tail = $after.Substring($after.IndexOf('Window: Edit'))
                if ($tail.Contains('Button: Cancel')) { $tail = $tail.Substring(0, $tail.IndexOf('Button: Cancel')) }
                Assert-That (-not ($tail -match 'Button: Make default')) 'after the click in the dialog the Make default button is gone'
            }
        }
        Invoke-El (Find-Name 'Cancel')
        Start-Sleep -Seconds 2
        if ($found) { break }
    }
    Assert-That $found 'an edit dialog of a row that is not the default has a Make default button'
    Start-Sleep -Seconds 1
    Assert-That ((Count-Buttons 'Make default') -eq 1) 'the list shows one row without the mark after the dialog click'
} finally { Stop-App }
if ($script:Failed) { exit 1 }
