param([string]$Exe, [string]$Menu = 'Post', [int]$Published = 1, [int]$Drafts = 1, [string]$NewTitle = 'Click Through Post')
# Clicks through the list screen of a content table (ContentTables) in a generated WinUI3 or Uno (Windows target) app over a scratch copy of Schemas/content.sql
# (one published post and one draft), with UI Automation: powershell -File winui3_content_check.ps1 -Exe <app.exe> (the Uno app needs the API running).
# Checks that each row has the buttons its state allows (Publish for a draft, Unpublish for a published row, Archive for both), that a click changes the buttons of that row and that
# Add New opens a form with a slug box that may stay blank and no box for the status. -Published and -Drafts say how many rows of each state the table starts with.
# It then saves a new row with a blank slug; read the rows back from the database afterwards (the script only writes through the app): the slug is the title as a slug and the status is draft.
Import-Module C:\Github\CodeGenNew\Docs\Verification\UiAutomation.psm1 -Force -WarningAction Ignore
$ErrorActionPreference = 'Stop'

function Count-Buttons([string]$name) { @(Find-Type 'Button' | Where-Object { $_.Current.Name -eq $name }).Count }
function First-Button([string]$name) { @(Find-Type 'Button' | Where-Object { $_.Current.Name -eq $name })[0] }

Start-App -Path $Exe | Out-Null
try {
    Start-Sleep -Seconds 5
    Select-El (Find-Name $Menu)
    Start-Sleep -Seconds 4
    $rows = $Published + $Drafts
    Assert-That ((Count-Buttons 'Publish') -eq $Drafts) 'a draft has a Publish button and a published row has none'
    Assert-That ((Count-Buttons 'Unpublish') -eq $Published) 'a published row has an Unpublish button and a draft has none'
    Assert-That ((Count-Buttons 'Archive') -eq $rows) 'every row that is not archived has an Archive button'

    Invoke-El (First-Button 'Publish')
    Start-Sleep -Seconds 3
    Assert-That ((Count-Buttons 'Publish') -eq ($Drafts - 1)) 'the published row lost its Publish button'
    Assert-That ((Count-Buttons 'Unpublish') -eq ($Published + 1)) 'the published row gained an Unpublish button'

    Invoke-El (First-Button 'Unpublish')
    Start-Sleep -Seconds 3
    Assert-That ((Count-Buttons 'Publish') -eq $Drafts) 'unpublishing brings the Publish button back'

    Invoke-El (First-Button 'Archive')
    Start-Sleep -Seconds 3
    Assert-That ((Count-Buttons 'Archive') -eq ($rows - 1)) 'an archived row has no Archive button'
    Assert-That ((Count-Buttons 'Publish') -ge 1) 'an archived row can be published again'

    # the edit dialog of a saved row has the same buttons for the row's state; a click changes the state shown and keeps the form open
    function Read-Dialog([string]$title) {
        $all = (List-Names | Select-Object -First 300) -join ' | '
        $dialog = $all.Substring($all.IndexOf("Window: $title"))
        $dialog.Substring(0, $dialog.IndexOf('Button: Cancel'))
    }
    Invoke-El (First-Button 'Edit')
    Start-Sleep -Seconds 3
    $dialog = Read-Dialog 'Edit'
    Write-Host $dialog
    Assert-That ($dialog -match 'Text: (draft|published|archived)') 'the edit dialog shows the status as text'
    $state = $Matches[1]
    $expected = @{ draft = @('Publish', 'Archive'); published = @('Unpublish', 'Archive'); archived = @('Publish') }[$state]
    foreach ($name in 'Publish', 'Unpublish', 'Archive') {
        Assert-That (($dialog -match "Button: $name") -eq ($expected -contains $name)) "the edit dialog of a $state row has the $name button only when it would change the state"
    }
    $press = $expected[0]
    $button = @(Find-Type 'Button' -Scope (Find-Name 'Edit Post') | Where-Object { $_.Current.Name -eq $press })[0]
    Invoke-El $button
    Start-Sleep -Seconds 3
    $dialog = Read-Dialog 'Edit'
    Assert-That (-not ($dialog -match "Text: $state")) "pressing $press in the edit dialog changed the status shown"
    Invoke-El (Find-Name 'Cancel')
    Start-Sleep -Seconds 2

    Invoke-El (Find-Name 'Add New')
    Start-Sleep -Seconds 3
    $form = Read-Dialog 'Add'
    Assert-That ($form -match 'Edit: Slug') 'the form has a slug box'
    Assert-That (-not ($form -match 'Edit: Status')) 'the form has no box for the status, only its text'
    Assert-That (-not ($form -match 'Button: (Publish|Unpublish|Archive)')) 'a new row has no state buttons in its form'
    Write-Host $form

    # a new row with a blank slug: the app makes the slug from the title and saves a draft
    Set-Text (Find-Name 'Title') $NewTitle
    Send-Keys '{TAB}'
    Select-El (Find-Name 'Notes')
    Start-Sleep -Seconds 1
    Set-Text (Find-Name 'Body') 'Written by the click-through'
    Send-Keys '{TAB}'
    Invoke-El (Find-Name 'Save')
    Start-Sleep -Seconds 3
} finally { Stop-App }
if ($script:Failed) { exit 1 }
